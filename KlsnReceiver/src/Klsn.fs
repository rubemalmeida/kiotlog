(*
    Copyright (C) 2017 Giampaolo Mancini, Trampoline SRL.
    Copyright (C) 2017 Francesco Varano, Trampoline SRL.

    This file is part of Kiotlog.

    Kiotlog is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Kiotlog is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

namespace KlsnReceiver

open System
open Chessie.ErrorHandling
open Microsoft.EntityFrameworkCore

open Sodium
open KiotlogDB
open Catalog
open SnPacket
open Helpers

module Klsn =

    type KlsnRequest =
        {
            TopicParts : ( string * string * string ) option
            Msg : byte [] option
            Device : Devices option
            Key : byte [] option
            Packet : SnPacket option
            Time : DateTime option
            Payload : byte [] option
        }

    let log what twoTrackInput =
        let now = DateTime.Now.ToUniversalTime().ToString("o")

        let header =
            match what with
            | "payload" -> "PAYLOAD"
            | "request" -> "REQUEST"
            | _ -> "NONE"

        let success (x, _) =
            let topic =
                let channel, app, device = x.TopicParts.Value
                sprintf "/%s/%s/%s" channel app device

            let message, nonce, data, time, payload =
                        x.Msg.Value |> hexStringFromByteArray,
                        x.Packet.Value.Nonce |> hexStringFromByteArray,
                        x.Packet.Value.Data |> hexStringFromByteArray,
                        x.Time.Value.ToString("o"),
                        x.Payload.Value |> Convert.ToBase64String

            let msg =
                match what with
                | "payload" -> x.Payload.Value |> Convert.ToBase64String
                | "request" ->
                    sprintf
                        "{ topic: %s, message: %s, nonce: %s, data: %s, time: %A, payload: %s }"
                        topic message nonce data time payload
                | _ -> "Hello, World!"

            let _, _, device = x.TopicParts.Value
            printfn "%s - [%s] [%s] %A" header now device msg

        let failure msgs =
            eprintfn "%s - [%A] ERRORS: %A" header now msgs

        eitherTee success failure twoTrackInput

    let parseRequest (cs : string) ctx =

        let optionsBuilder = DbContextOptionsBuilder<KiotlogDBContext>()
        optionsBuilder.UseNpgsql(cs) |> ignore

        use dbCtx = new KiotlogDBContext(optionsBuilder.Options)

        let devices = getDevices dbCtx

        let getDevice req =
            let _ ,_, device = req.TopicParts.Value
            match getDevice device devices with
            | Ok (x, _) -> ok { req with Device = Some x }
            | Bad msgs -> fail ( sprintf "%A" msgs)

        let getKey req =
            try
                let key = req.Device.Value.Auth.Klsn.Key |> Convert.FromBase64String
                ok { req with Key = Some key }
            with
                | _ -> fail "Key not found"

        let parseMsg req =
            try
                let packet = parseSnPacket req.Msg.Value
                ok { req with Packet = Some packet }
            with
                | :? InvalidOperationException as ex ->
                    sprintf "MsgPack Deserialization failed : %s" ex.Message |> fail

        let tryDecrypt req =
            let data, nonce, key =
                req.Packet.Value.Data,
                req.Packet.Value.Nonce,
                req.Key.Value
            try
                let plain = SecretAeadIETF.Decrypt(data, nonce, key)
                ok { req with Payload = Some plain }
            with
                | _ -> fail "AEAD Failed"

        let validateRequest =
            getDevice
            >> bind getKey
            >> bind parseMsg
            >> bind tryDecrypt

        ctx
        |> validateRequest

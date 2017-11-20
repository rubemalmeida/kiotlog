namespace Decoder

open Microsoft.EntityFrameworkCore

open KiotlogDB
open System

module Catalog =

    let getDevices (ctx : KiotlogDBContext)  =
        ctx.Devices
            .Include("Sensors")
            .Include("Sensors.SensorType")
            .Include("Sensors.Conversion")
    
    let getDevice devId (devices : Linq.IQueryable<Devices>)  =
        let device =
            try
                query {
                    for d in devices do
                    where (d.Device = devId)
                    select d
                    exactlyOne
                } |> Some
            with
                | :? InvalidOperationException -> None
        device
       
    let getSortedSensors (device : Devices) =  
        device.Sensors
        |> Seq.sortBy (fun sensor -> sensor.Fmt.Index)

    let getFormatString (device : Devices) =
        let endianness = if device.Frame.Bigendian then ">" else "<"

        let fmtString =
            device
            |> getSortedSensors
            |> Seq.map (fun sensor -> sensor.Fmt.FmtChr)
            |> Seq.reduce (+)
        
        endianness + fmtString

    let writePoint cs (device, time, flags, data) =
        match data with
        | None -> ()
        | Some data ->
            use ctx = new KiotlogDBContext(cs)

            Points (
                DeviceDevice = device,
                Time = time,
                Flags = flags,
                Data = data )
            |> ctx.Points.Add |> ignore
        
            ctx.SaveChanges() |> ignore
// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Akka.Actor
open Akka.FSharp

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave.Logging
open FSharp.Json
open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Utils

open Suave.Writers
open Newtonsoft.Json

type MessageType = {
    OperationType : string
    UserName : string
    Password : string
    followUser : string
    TweetMsg : string
    Query : string
}

type ResponseType = {
    Status : string
    Data : string
}

type OperationsActor =
    | Operate of MessageType* WebSocket


let system = ActorSystem.Create("TwitterServer")





let webSocket (webSocket : WebSocket) (context: HttpContext) = 
    socket{
        let mutable on = true
        while on do
            let! msg = webSocket.read()

            match msg with
            | (Text, data, true) -> 
                let incomingData = UTF8.toString data
                let json = Json.deserialize<MessageType> incomingData

                printfn "%s" json.OperationType
                let mutable operationType = json.OperationType
                let mutable username = json.UserName
                let mutable password = json.Password
                let mutable tweetData = json.TweetMsg

                let task = operationActor <? Operate(json, webSocket)
                let response: ResponseType = Async.RunSynchronously(task, 10000)

                let responseBytes =
                    Json.serialize response
                    |> System.Text.Encoding.ASCII.GetBytes
                    |> ByteSegment

                do! webSocket.send Text responseBytes true

            | (Close,_,_) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
    }

let app : WebPart = 
  choose [
    path "/websocket" >=> handShake webSocket
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") webSocket
    //path "/websocketWithError" >=> handShake wsWithErrorHandling
    GET >=> choose [
         pathScan "/query/%s/%s" handleQuery
         pathScan "/queryhashtags/%s" handleQueryHashtags 
         pathScan "/querymentions/%s" handleQueryMentions  
         ]
    POST >=> choose [
         path "/sendtweet" >=> Test  
         ]
    NOT_FOUND "Found no handlers." ]


[<EntryPoint>]
let main _ =
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app
  0
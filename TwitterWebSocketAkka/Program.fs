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

open System.Collections.Generic
open System.Collections

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

type RegisterActor =
    | Register of string*string*WebSocket

type OperationsActor =
    | Operate of MessageType* WebSocket


let system = ActorSystem.Create("TwitterServer")

type UserSystem() =

    let userPasswordMap = new Dictionary<string,string>()
    let userSocketMap = new Dictionary<string, WebSocket>()
    let mutable activeUsersSet = Set.empty
    let userFollowerList = new Dictionary<string, Dictionary<string,string>>()
    let hashTagFollowerList = new Dictionary<string, Dictionary<string, string>>()
    let userIpPortMap = new Dictionary<string, string>()


    member this.Register username password websocket=
        // printfn "in user system"
        let mutable response:ResponseType = {Status=""; Data=""}
        if userPasswordMap.ContainsKey(username) then
            response <- {Status="Fail"; Data="Username already exists!"}
        else
            let followerDict = new Dictionary<string,string>()
            userPasswordMap.Add(username, password)
            userFollowerList.Add(username, followerDict)
            userSocketMap.Add(username, websocket)
            response <- {Status="Success"; Data= sprintf "%s added successfully" username}
        // printfn "%A" response
        response

let userSystem = UserSystem()




type Twitter() = 
    let mutable userPasswordDict = new Dictionary<string,string>()

    member this.Register username password websocket=
        let mutable response:ResponseType = {Status=""; Data=""}
        if userPasswordDict.ContainsKey(username) then
            response <- {Status="Fail"; Data="Username already exists!"}
        else
            userPasswordDict.Add(username, password)
            response <- {Status="Success"; Data= sprintf "%s added successfully" username}

let twitter = Twitter()



let RegisterActor (mailbox: Actor<_>) = 
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        match msg  with
        |   Register(username,password,webSocket) ->
            if username = "dummy" then
                return! loop()

                
            mailbox.Sender() <? userSystem.Register username password webSocket|> ignore
        | _ ->  failwith "Invalid Operation "
        return! loop()     
    }
    loop ()


let OperationsActor (mailbox: Actor<_>) = 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()

        match msg with
        | Operate(data, webSocket) ->
            let operationType = data.OperationType
            let username = data.UserName
            let password = data.Password
            let followUser = data.followUser
            let tweetmsg = data.TweetMsg
            let query = data.Query
            let actorPath =  @"akka://TwitterServer/user/registerActor"
            let registerActor = select actorPath system
            let mutable task = registerActor <? Register("dummy","", webSocket)
            match operationType with
            | "register" ->
                printfn "[Operation: Register] username=%s, password=%s" username password
                let actorPath =  @"akka://TwitterServer/user/registerActor"
                let registerActor = select actorPath system
                task <- registerActor <? Register(username, password, webSocket)
                let response: ResponseType = Async.RunSynchronously (task, 1000)
                sender <? response |> ignore
                printfn "Register response %s : %s" response.Status response.Data


        return! loop()

                
    }
    loop()



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

                let actorPath =  @"akka://TwitterServer/user/operationsActor"
                let operationsActor = select actorPath system

                let task = operationsActor <? Operate(json, webSocket)
                let response: ResponseType = Async.RunSynchronously(task, 10000)
                // printfn " in websocket %A" response 
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
    // path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") webSocket
    // //path "/websocketWithError" >=> handShake wsWithErrorHandling
    // GET >=> choose [
    //      pathScan "/query/%s/%s" handleQuery
    //      pathScan "/queryhashtags/%s" handleQueryHashtags 
    //      pathScan "/querymentions/%s" handleQueryMentions  
    //      ]
    // POST >=> choose [
    //      path "/sendtweet" >=> Test  
    //      ]
    NOT_FOUND "Found no handlers." ]


[<EntryPoint>]
let main _ =
  let operationsActor = spawn system "operationsActor" OperationsActor
  let registerActor = spawn system "registerActor" RegisterActor
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app

  0
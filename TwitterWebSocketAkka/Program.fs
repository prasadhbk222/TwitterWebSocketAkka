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

type UserSystemActor =
    | Register of string*string*WebSocket
    | Login of string*string*WebSocket
    | Logout of string*WebSocket

type OperationsActor =
    | Operate of MessageType* WebSocket


let system = ActorSystem.Create("TwitterServer")

// type UserSystem() =

//     let userPasswordMap = new Dictionary<string,string>()
//     let userSocketMap = new Dictionary<string, WebSocket>()
//     let mutable activeUsersSet = Set.empty
//     let userFollowerList = new Dictionary<string, Dictionary<string,string>>()
//     let hashTagFollowerList = new Dictionary<string, Dictionary<string, string>>()
//     let userIpPortMap = new Dictionary<string, string>()


//     member this.Register username password websocket=
//         // printfn "in user system"
//         let mutable response:ResponseType = {Status=""; Data=""}
//         if userPasswordMap.ContainsKey(username) then
//             response <- {Status="Fail"; Data="Username already exists!"}
//         else
//             let followerDict = new Dictionary<string,string>()
//             userPasswordMap.Add(username, password)
//             userFollowerList.Add(username, followerDict)
//             userSocketMap.Add(username, websocket)
//             response <- {Status="Success"; Data= sprintf "%s added successfully" username}
//         // printfn "%A" response
//         response

// let userSystem = UserSystem()




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



let UserSystemActor (mailbox: Actor<_>) = 
    let userPasswordMap = new Dictionary<string,string>()
    let userSocketMap = new Dictionary<string, WebSocket>()
    let mutable activeUsersSet = Set.empty
    let userFollowerList = new Dictionary<string, Dictionary<string,string>>()
    let hashTagFollowerList = new Dictionary<string, Dictionary<string, string>>()
    let userIpPortMap = new Dictionary<string, string>()
    let mutable response:ResponseType = {Status=""; Data=""}
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        printfn "%A" msg
        match msg  with
        |Register(username,password,webSocket) ->
            if username = "dummy" then
                return! loop()

            if userPasswordMap.ContainsKey(username) then
                response <- {Status="Fail"; Data="Username already exists!"}
            else
                let followerDict = new Dictionary<string,string>()
                userPasswordMap.Add(username, password)
                userFollowerList.Add(username, followerDict)
                userSocketMap.Add(username, webSocket)
                response <- {Status="Success"; Data= sprintf "%s added successfully" username}
            // printfn "%A" response

            // mailbox.Sender() <? response |> ignore

        | Login(username,password,webSocket) ->
            if userPasswordMap.ContainsKey(username) then
                if (password = userPasswordMap.[username]) then
                    if (activeUsersSet.Contains(username)) then
                        response <- {Status="Fail"; Data= sprintf "%s already logged in" username}
                    else
                        activeUsersSet <- activeUsersSet.Add(username)
                        response <- {Status="Success"; Data= sprintf "%s logged in successfully" username}
                else
                    response <- {Status="Fail"; Data= sprintf "%s Wrong Password" username}
            else
                response <- {Status="Fail"; Data= sprintf "%s not registered" username}

        
        | Logout(username, WebSocket) ->
            if userPasswordMap.ContainsKey(username) then
                if (activeUsersSet.Contains(username)) then
                    activeUsersSet <- activeUsersSet.Remove(username)
                    response <- {Status="Success"; Data= sprintf "%s logged out successfully" username}
                else
                    response <- {Status="Fail"; Data= sprintf "%s not loggedin" username}
            else
                response <- {Status="Fail"; Data= sprintf "%s not registered" username}






        | _ ->  failwith "Invalid Operation "

        sender <? response |> ignore
        return! loop()     
    }
    loop ()


let OperationsActor (mailbox: Actor<_>) = 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        printfn "%A" msg

        match msg with
        | Operate(data, webSocket) ->
            let operationType = data.OperationType
            let username = data.UserName
            let password = data.Password
            let followUser = data.followUser
            let tweetmsg = data.TweetMsg
            let query = data.Query
            let actorPath =  @"akka://TwitterServer/user/userSystemActor"
            let userSystemActor = select actorPath system
            let mutable task = userSystemActor <? Register("dummy","", webSocket)
            match operationType with
            | "register" ->
                printfn "[Operation: Register] username=%s, password=%s" username password
                task <- userSystemActor <? Register(username, password, webSocket)
                let response: ResponseType = Async.RunSynchronously (task, 1000)
                sender <? response |> ignore
                printfn "Register response %s : %s" response.Status response.Data

            | "login" ->
                printfn "[Operation: Login] username=%s, password=%s" username password
                task <- userSystemActor <? Login(username, password, webSocket)
                let response: ResponseType = Async.RunSynchronously (task, 1000)
                sender <? response |> ignore
                printfn "Login response %s : %s" response.Status response.Data

            | "logout" ->
                printfn "[Operation: Logout] username=%s" username
                task <- userSystemActor <? Logout(username, webSocket)
                let response: ResponseType = Async.RunSynchronously (task, 1000)
                sender <? response |> ignore
                printfn "Logout response %s : %s" response.Status response.Data





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
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") webSocket
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
  let userSystemActor = spawn system "userSystemActor" UserSystemActor
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app

  0
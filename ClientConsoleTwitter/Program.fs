// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Akka.Actor
open Akka.FSharp
open Akka.Remote
open Akka.Configuration
open WebSocketSharp
open FSharp.Json


let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : OFF
            loglevel : OFF
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8555
                    hostname = localhost
                }
            }
        }")

type TwitterClientInstructions = 
    | Register of string*string    //username, password
    | Login of string*string    //username, password
    | Logout        
    | FollowUser of string        //tofollow
    | Tweet of string        //tweet
    | Query of string        //ht por mention


type MessageType = {
    OperationType : string
    UserName : string
    Password : string
    followUser : string
    TweetMsg : string
    Query : string
}

let TwitterClient (webSocket: WebSocket )(mailbox: Actor<_>) = 
    let mutable UserName = ""
    let mutable Password = ""

    let rec loop() = actor {
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()

        match message with
        | Register (username, password) ->
            UserName <- username
            Password <- password
            let ogJson: MessageType = {OperationType = "register"; UserName = UserName; Password = Password; 
                                        followUser = ""; TweetMsg = ""; Query = ""}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data

        | Login (username, password) ->
            UserName <- username
            Password <- password
            let ogJson: MessageType = {OperationType = "login"; UserName = UserName; Password = Password; 
                                        followUser = ""; TweetMsg = ""; Query = ""}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data
            
        | Logout ->
            
            let ogJson: MessageType = {OperationType = "logout"; UserName = UserName; Password = Password; 
                                        followUser = ""; TweetMsg = ""; Query = ""}
            UserName <- ""
            Password <- ""
            let json_data = Json.serialize ogJson
            webSocket.Send json_data

        | FollowUser followUser ->
            let ogJson: MessageType = {OperationType = "follow"; UserName = UserName; Password = Password; 
                                        followUser = followUser; TweetMsg = ""; Query = ""}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data

        | Tweet msg ->
            let ogJson: MessageType = {OperationType = "tweet"; UserName = UserName; Password = Password; 
                                        followUser = ""; TweetMsg = msg; Query = ""}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data

        | Query tag ->
            let ogJson: MessageType = {OperationType = "query"; UserName = UserName; Password = Password; 
                                        followUser = ""; TweetMsg = ""; Query = tag}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data

            
        return! loop()

    }
    loop()

let rec takeInput(twitterClient) =
    Console.Write("What do you want to do? \n")
    let input = Console.ReadLine().Split ','
    let operationType = input.[0]

    match operationType with
    | "Register" ->
        let username = input.[1]
        let password = input.[2]
        twitterClient <! Register (username, password)

    | "Login" ->
        let username = input.[1]
        let password = input.[2]
        twitterClient <! Login (username, password)

    | "Logout" ->
        // let username = input.[1]
        twitterClient <! Logout

    | "Follow" ->
        let username = input.[1]
        twitterClient <! FollowUser username

    | "Tweet" ->
        let tweet = input.[1]
        twitterClient <! Tweet tweet

    | "Query" ->
        let tag = input.[1]
        twitterClient <! Query tag

    | _ ->
        printfn "aa"


    takeInput(twitterClient)


[<EntryPoint>]
let main argv =
    let clientSystem = ActorSystem.Create("clientSystem", configuration)
    let webSocket = new WebSocket("ws://localhost:8080/websocket")
    let client = spawn clientSystem "user" (TwitterClient webSocket)
    webSocket.OnOpen.Add(fun args -> System.Console.WriteLine("Open"))
    webSocket.OnClose.Add(fun args -> System.Console.WriteLine("Close"))
    webSocket.OnMessage.Add(fun args -> System.Console.WriteLine("Msg: {0}", args.Data))
    webSocket.OnError.Add(fun args -> System.Console.WriteLine("Error: {0}", args.Message))
    webSocket.Connect()
    takeInput(client)


    0
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

type MessageType = {
    OperationType : string
    UserName : string
    Password : string
    followUser : string
    TweetMsg : string
    Query : string
}

let TwitterClient (webSocket: WebSocket )(mailbox: Actor<_>) = 
    let mutable userName = ""
    let mutable password = ""

    let rec loop() = actor {
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()

        match message with
        | Register (username, password) ->
            let ogJson: MessageType = {OperationType = "register"; UserName = username; Password = password; 
                                        followUser = ""; TweetMsg = ""; Query = ""}
            let json_data = Json.serialize ogJson
            webSocket.Send json_data
            


        return! loop()




    }
    loop()

[<EntryPoint>]
let main argv =
    let clientSystem = ActorSystem.Create("clientSystem", configuration)
    let webSocket = new WebSocket("ws://localhost:8080/websocket")
    let client = spawn clientSystem "user" TwitterClient webSocket
    webSocket.OnOpen.Add(fun args -> System.Console.WriteLine("Open"))
    webSocket.OnClose.Add(fun args -> System.Console.WriteLine("Close"))
    webSocket.OnMessage.Add(fun args -> System.Console.WriteLine("Msg: {0}", args.Data))
    webSocket.OnError.Add(fun args -> System.Console.WriteLine("Error: {0}", args.Message))
    webSocket.Connect()


    0
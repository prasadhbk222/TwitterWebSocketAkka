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
    | Follow of string*string
    | GetFollowers of string
    

type TweetsActor=
    | Tweet of string*string

type TweetsParsorActor=
    | ParseTweet of int*string*string


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

let TweetsParserActor (mailbox: Actor<_>) =
    //printfn "abc"
    let hashTagsTweetMap = new Dictionary<string, List<string>>()  // hashtag tweet ids  map for querying
    let mentionsTweetMap = new Dictionary<string, List<string>>()  // mentions tweet ids map for querying 
    let rec loop() = actor{
        let! message = mailbox.Receive()
        printfn "@@@@@@@@@@@@@@@@@@@@@ in tweets parser"
        match message with
        | ParseTweet (tweetId,username, tweet) ->
            let words = tweet.Split ' '
            let listHashTags = new List<string>()
            //let listMentions = new List<string>()
            let dictMentions = new Dictionary<string, string>()
            for word in words do
                if word.[0] = '#' then
                    listHashTags.Add(word)
                if word.[0] = '@' then
                    dictMentions.Add(word.Substring(1), word.Substring(1))

            for hashTag in listHashTags do
                //printfn "@@@@Parsed hashtag is %s" hashTag
                if hashTagsTweetMap.ContainsKey(hashTag) then 
                    hashTagsTweetMap.[hashTag].Add(tweet)
                else
                    let listTweet = new List<string>()
                    listTweet.Add(tweet)
                    hashTagsTweetMap.Add(hashTag, listTweet)
                for ht in hashTagsTweetMap do
                    for tweet in ht.Value do
                        printfn "%s %s" ht.Key tweet

            for  mention in dictMentions do
                //printfn "@@@@Parsed hashtag is %s" hashTag
                let actualmention =  "@" + mention.Key
                if mentionsTweetMap.ContainsKey(actualmention) then 
                    mentionsTweetMap.[actualmention].Add(tweet)
                else
                    let listTweet = new List<string>()
                    listTweet.Add(tweet)
                    mentionsTweetMap.Add(actualmention, listTweet)
                for mention in mentionsTweetMap do
                    for tweet in mention.Value do
                        printfn "%s %s" mention.Key tweet
            
            // let actorPath =  @"akka://twitterSystem/user/tweetsRef"
            // let tweetsRef = select actorPath twitterSystem
            // tweetsRef <! ReceiveHashTags(userId, tweetId, listHashTags)
            // tweetsRef <! ReceiveMentions(userId, tweetId, dictMentions)


        return! loop()
    }
    loop()


let TweetsActor (userSystemActor:IActorRef) (mailbox: Actor<_>) =
    let tweetsMap = new Dictionary<int,string>()
    let tweetsUserMap = new Dictionary<int, string>()
    let hashTagsTweetMap = new Dictionary<string, List<int>>()  // hashtag tweet ids  map for querying
    let mentionsTweetMap = new Dictionary<string, List<int>>()  // mentions tweet ids map for querying
    let userTweetMap = new Dictionary<string, List<String>>()
    let mutable tweetId = 0;
    let actorPath =  @"akka://TwitterServer/user/tweetsParserActor"
    let tweetsParserActor = select actorPath system

    let rec loop() = actor{
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()

        match message with
        | Tweet(username, tweetmsg) ->
            printfn "@@@@in TweetsActor"
            tweetId <- tweetId + 1
            tweetsMap.Add(tweetId, tweetmsg)
            tweetsUserMap.Add(tweetId, username)
            if (userTweetMap.ContainsKey(username)) then
                userTweetMap.[username].Add(tweetmsg)
            else
                let listTweet = new List<string>()
                listTweet.Add(tweetmsg)
                userTweetMap.Add(username, listTweet)

            
            tweetsParserActor <! ParseTweet(tweetId, username, tweetmsg)
            let promise = userSystemActor <? GetFollowers(username)
            let followerSocket: Dictionary<String,WebSocket> = Async.RunSynchronously(promise, 10000)
            for socket in followerSocket do
                printfn "%s : %A" socket.Key socket.Value
            sender <? followerSocket |> ignore
             

        return! loop()
    }
    loop()







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
        printfn " in UserSystems Actor => %A" msg
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

        | Follow(username, userIdOfFollowed) ->
            if userFollowerList.ContainsKey(userIdOfFollowed) then
                let followerDict = userFollowerList.[userIdOfFollowed]
                if  not <| followerDict.ContainsKey(username) then
                    followerDict.Add(username, username);
                    response <- {Status="Success"; Data= sprintf "%s followed %s" username userIdOfFollowed}
                    // printfn "%A" response

        | GetFollowers(username) ->
            printfn "@@@@in get followers"
            if userFollowerList.ContainsKey(username) then
                let followerSocketDict = new Dictionary<string, WebSocket>()
                let followerDict = userFollowerList.[username]
                for follower in followerDict do
                    followerSocketDict.Add(follower.Key, userSocketMap.[follower.Key])
                sender <? followerSocketDict |> ignore
                return! loop()
            

        | _ ->  failwith "Invalid Operation "

        sender <? response |> ignore
        return! loop()     
    }
    loop ()


let OperationsActor (mailbox: Actor<_>) = 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        printfn " in Operations Actor => %A" msg

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
            let actorPath_tweetsActor =  @"akka://TwitterServer/user/tweetsActor"
            let tweetsActor = select actorPath_tweetsActor system
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

            | "follow" ->
                printfn "[Operation: follow] username=%s" username
                task <- userSystemActor <? Follow (username,followUser)
                let response: ResponseType = Async.RunSynchronously (task, 1000)
                sender <? response |> ignore
                printfn "follow response %s : %s" response.Status response.Data

            | "tweet" ->
                printfn "[Operation: tweet] username=%s" username
                let promise = tweetsActor <? Tweet (username,tweetmsg)
                let response : Dictionary<String,WebSocket> = Async.RunSynchronously (promise, 1000)
                sender <? response |> ignore
                // printfn "tweet response %s : %s" response.Status response.Data




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

                
                
                if (operationType = "tweet") then
                    let task = operationsActor <? Operate(json, webSocket)
                    let followerSockets: Dictionary<string,WebSocket> = Async.RunSynchronously(task, 10000)
                    let responseBytes=
                        (sprintf "tweet: %s by %s" tweetData username)
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    for socket in followerSockets do
                        do! socket.Value.send Text responseBytes true

                else 
                    let task = operationsActor <? Operate(json, webSocket)
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
  let tweetsActor = spawn system "tweetsActor" (TweetsActor userSystemActor)
  let tweetsParserActor = spawn system "tweetsParserActor" TweetsParserActor
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app

  0
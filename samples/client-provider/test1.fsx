// Uncomment those to use build script client assembly
//#r "../../bin/FSharp.Data.GraphQL.Client/ne47/FSharp.Data.GraphQL.Client.dll"
//#r "../../bin/FSharp.Data.GraphQL.Shared/ne47/FSharp.Data.GraphQL.Shared.dll"

// Uncomment those to use dotnet build command for the client assembly
#r "../../src/FSharp.Data.GraphQL.Shared/bin/Debug/net47/FSharp.Data.GraphQL.Shared.dll"
#r "../../src/FSharp.Data.GraphQL.Client/bin/Debug/net47/FSharp.Data.GraphQL.Client.dll"

open FSharp.Data.GraphQL

type MyProvider = GraphQLProvider<"http://localhost:8084">

let x = MyProvider.Types.Ball("Circular", "Ball")

printfn "%A\n" x

let runtimeUrl = "http://localhost:8084"

let ctx = MyProvider.GetContext(runtimeUrl)

// Change method name to Operation
let res = 
    ctx.Operation<"""query q {
      hero (id: "1000") {
        name
        appearsIn
        homePlanet
        friends {
          ... on Human {
            name
            homePlanet
          }
          ... on Droid {
            name
            primaryFunction
          }
        }
      }
    }""">()
  
printfn "Headers: %A" res.CustomHttpHeaders
printfn "Server: %s\n" res.ServerUrl

let result = res.Run()

let data = result.Data

printfn "Data: %A\n" data

let hero = data.Hero.Value

// Map custom response types other than data (errors, documentId)
// Map variables
// Produce XML docs from the schema
// Make deprecated fields
// Make enum values literals
// Make run async methods
if hero.AppearsIn |> Array.exists (fun x -> x = MyProvider.Types.Episode.Empire)
then printfn "Hero appears in Empire episode!\n"
else printfn "Hero does not appear in Empire episode!\n"

let friends = hero.Friends |> Array.choose id

//let thisWillGetAnError = friends |> Array.map (fun x -> x.AsDroid())

let humanFriends = friends |> Array.choose (fun x -> x.TryAsHuman())

let droidFriends = friends |> Array.choose (fun x -> x.TryAsDroid())

let humanFriendsCount = friends |> Array.map (fun x -> if x.IsHuman() then 1 else 0) |> Array.reduce (+)

let droidFriendsCount = friends |> Array.map (fun x -> if x.IsDroid() then 1 else 0) |> Array.reduce (+)

printfn "Hero friends (%i): %A\n" friends.Length friends

printfn "Hero human friends (%i): %A\n" humanFriendsCount humanFriends

printfn "Hero droid friends (%i): %A\n" droidFriendsCount droidFriends
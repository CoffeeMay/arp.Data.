// Uncomment those to use build script client assembly
//#r "../../bin/FSharp.Data.GraphQL.Client/net47/FSharp.Data.GraphQL.Client.dll"
//#r "../../bin/FSharp.Data.GraphQL.Shared/net47/FSharp.Data.GraphQL.Shared.dll"

// Uncomment those to use build script client assembly using netstandard2.0
//#r "../../bin/FSharp.Data.GraphQL.Shared/netstandard2.0/FSharp.Data.GraphQL.Shared.dll"
//#r "../../bin/FSharp.Data.GraphQL.Client/netstandard2.0/netstandard.dll"
//#r "../../bin/FSharp.Data.GraphQL.Client/netstandard2.0/FSharp.Data.GraphQL.Client.dll"

// Uncomment those to use dotnet build command for the client assembly
// #r "../../src/FSharp.Data.GraphQL.Shared/bin/Debug/net47/FSharp.Data.GraphQL.Shared.dll"
// #r "../../src/FSharp.Data.GraphQL.Client/bin/Debug/net47/FSharp.Data.GraphQL.Client.dll"

//Uncomment those to use dotnet build command for the client assembly using netstandard2.0
#r "../../src/FSharp.Data.GraphQL.Shared/bin/Debug/netstandard2.0/FSharp.Data.GraphQL.Shared.dll"
#r "../../src/FSharp.Data.GraphQL.Client/bin/Debug/netstandard2.0/netstandard.dll"
#r "../../src/FSharp.Data.GraphQL.Client/bin/Debug/netstandard2.0/FSharp.Data.GraphQL.Client.dll"

open FSharp.Data.GraphQL

// The URL here is for design time purposes.
// It connects to the server to be able to map its schema.
//type MyProvider = GraphQLProvider<"http://localhost:8084">

// You can also provide the introspection schema yourself if you can't access the server
// at design time. Just provide a file in the path of the project or a literal containing
// the introspection query result.
// WARNING: the introspection query result must contain all fields requested by the
// standard introspection query string in FSharp.Data.GraphQL.Introspection.IntrospectionQuery (FSharp.Data.GraphQL.Shared Assembly).
type MyProvider = GraphQLProvider<"sample_schema.json">

// Once mapped, all custom types of the schema (types that are not scalar types)
// will be mapped into CLR types. You can create those types by filling each of its
// properties into the constructor.

let ball = MyProvider.Types.Ball(form = "Spheric", format = "Spheric", id = "1")
let box = MyProvider.Types.Box(form = "Cubic", format = "Cubic", id = "2")

let things : MyProvider.Types.IThing list = [ball; box]

printfn "Things: %A\n" things

// Once created the provider and the schema is successfully mapped,
// We can start doing queries. You can optionally specify an runtime URL for the server.
// NOTE: if you use a local introspection json file, the runtime URL is required.
let runtimeUrl = "http://localhost:8084"

// A context exists for reusing the same schema against different servers by filling an
// runtime URL if needed. If not, the context will use the same static URL used for the
// provider definition.
let ctx = MyProvider.GetContext(runtimeUrl)

// The operation method can be used to make queries, mutations, and subscriptions.
// Although subscription operations can be created, the client provider still
// does not work with web sockets - only the immediate response will be known.
let operation = 
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

printfn "Server: %s\n" operation.ServerUrl

// To run an operation, you just need to call the Run or AsyncRun method.
let result = operation.Run()
//let result = operation.AsyncRun() |> Async.RunSynchronously

// If the operation runs without any error, result data will be on the Data property.
let data = result.Data

// If the operation does not have a Data (None), it could be failed on the server and the errors are mapped
// to the Error property.
let errors = result.Errors

// Custom fields are returned here (for example, documentId on our sample Giraffe Server.)
let customData = result.CustomData

// Query result objects have pretty-printing and structural equality.
printfn "Data: %A\n" data
printfn "Errors: %A\n" errors
printfn "Custom data: %A\n" customData

let hero = data.Value.Hero.Value

// GraphQL enum types are essentially strings, and here they are mapped to
// custom objects with string values inside. Each enum value does have an static
// instance of itself, and does have structural equality against other enum types.
// However, they are not classic CLR enum types.
if hero.AppearsIn |> Array.exists (fun x -> x = MyProvider.Types.Episode.Empire)
then printfn "Hero appears in Empire episode!\n"
else printfn "Hero does not appear in Empire episode!\n"

let friends = hero.Friends |> Array.choose id

// When we have interfaces or union types in the GraphQL schema, they are mapped as
// Inherited objects in the client. However, as the type provider uses erased types,
// we can't pattern match them by classic pattern matching. Instead, we use the following
// methods of types generated by GraphQL union or interface types:

// This will produce an error. Not all friends are droids!
//let thisWillProduceAnError = friends |> Array.map (fun x -> x.AsDroid())

// We can easily filter friends by using "TryAs" methods.
let humanFriends = friends |> Array.choose (fun x -> x.TryAsHuman())
let droidFriends = friends |> Array.choose (fun x -> x.TryAsDroid())

// We can also use "Is" version methods to do some custom matching.
let humanFriendsCount = friends |> Array.map (fun x -> if x.IsHuman() then 1 else 0) |> Array.reduce (+)
let droidFriendsCount = friends |> Array.map (fun x -> if x.IsDroid() then 1 else 0) |> Array.reduce (+)

printfn "Hero friends (%i): %A\n" friends.Length friends
printfn "Hero human friends (%i): %A\n" humanFriendsCount humanFriends
printfn "Hero droid friends (%i): %A\n" droidFriendsCount droidFriends

operation.Dispose()
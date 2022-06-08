﻿/// The MIT License (MIT)
/// Copyright (c) 2016 Bazinga Technologies Inc

module FSharp.Data.GraphQL.Tests.AsyncValTests

open System
open FSharp.Data.GraphQL
open Xunit
open FsCheck

[<Fact>]
let ``AsyncVal computation allows to return constant values`` () =
    let v = asyncVal { return 1 }
    v.IsAsync |> equals false
    v.IsSync |> equals true
    v.Value |> equals 1
    
[<Fact>]
let ``AsyncVal computation allows to return from async computation`` () =
    let v = asyncVal { return! async { return 1 } }
    v.IsAsync |> equals true
    v.IsSync |> equals false
    v |> AsyncVal.get |> equals 1
    
[<Fact>]
let ``AsyncVal computation allows to return from another AsyncVal`` () =
    let v = asyncVal { return! asyncVal { return 1 } }
    v.IsAsync |> equals false
    v.IsSync |> equals true
    v.Value |> equals 1

[<Fact>]
let ``AsyncVal computation allows to bind async computations`` () =
    let v = asyncVal { 
        let! value = async { return 1 }
        return value }
    v.IsAsync |> equals true
    v.IsSync |> equals false
    v |> AsyncVal.get |> equals 1

[<Fact>]
let ``AsyncVal computation allows to bind another AsyncVal`` () =
    let v = asyncVal { 
        let! value = asyncVal { return 1 }
        return value }
    v.IsAsync |> equals false
    v.IsSync |> equals true
    v.Value |> equals 1
    
[<Fact>]
let ``AsyncVal computation defines zero value`` () =
    let v = asyncVal { printf "aa" }
    v.IsAsync |> equals false
    v.IsSync |> equals true
    
[<Fact>]
let ``AsyncVal can be returned from Async computation`` () =
    let a = async { return! asyncVal { return 1 } }
    let res = a |> sync
    res |> equals 1
    
[<Fact>]
let ``AsyncVal can be bound inside Async computation`` () =
    let a = async { 
        let! v = asyncVal { return 1 }
        return v }
    let res = a |> sync
    res |> equals 1
/// The MIT License (MIT)
/// Copyright (c) 2016 Bazinga Technologies Inc

namespace FSharp.Data.GraphQL.Client

open System
open System.IO
open System.Net
open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices

open FSharp.Data.GraphQL.Types.Introspection
open TypeCompiler
open System.Collections.Generic
open Newtonsoft.Json.Linq
open Newtonsoft.Json
open Microsoft.FSharp.Quotations
open QuotationHelpers

module Util =
    open System.Text.RegularExpressions
    open FSharp.Data.GraphQL
    open QuotationHelpers

    let getOrFail (err: string) = function
        | Some v -> v
        | None -> failwith err

    let tryOrFail (err: string) (f: unit->'T) =
        try f()
        with ex -> Exception(err, ex) |> raise

    let firstToUpper (str: string) =
        if str <> null && str.Length > 0
        then str.[0].ToString().ToUpper() + str.Substring(1)
        else str

    let requestSchema (url: string) =
        async {
            let requestUrl = Uri(Uri(url), ("/?query=" + FSharp.Data.GraphQL.Introspection.introspectionQuery))
            let req = WebRequest.CreateHttp(requestUrl)
            req.Method <- "GET"
            use! resp = req.GetResponseAsync() |> Async.AwaitTask
            use stream = resp.GetResponseStream()
            use reader = new StreamReader(stream)
            let! json = reader.ReadToEndAsync() |> Async.AwaitTask
            let result = Serialization.fromJson json
            match result.Errors with
            | None ->
                let introspectionSchema = result.Data.__schema
                return Choice1Of2 introspectionSchema
            | Some errors ->
                return Choice2Of2 errors
        }

    let compileTypesFromSchema asm ns (schema: IntrospectionSchema) = 
        let underlyingType (t: TypeReference) =
            t.UnderlyingType
        let ctx = {
            Assembly = asm
            Namespace = ns
            KnownTypes = ProviderSessionContext.CoreTypes }
        let typeDefinitions =
            (ctx.KnownTypes, schema.Types)
            ||> Array.fold (fun acc t ->
                if acc.ContainsKey t.Name
                then acc
                else Map.add t.Name (ProvidedType (initType ctx t, t)) acc) 
        let defctx = { ctx with KnownTypes = typeDefinitions }
        typeDefinitions
        |> Seq.iter (fun kv ->
            match kv.Value with
            | NativeType t -> ()
            | ProvidedType (t, itype) ->
                genType defctx itype t)
        typeDefinitions

    let createMethod (tdef: ProvidedTypeDefinition) (schemaTypes: Map<string,TypeReference>)
                     (serverUrl: string) (opName: string) (opField: IntrospectionField) =
        let findType (t: IntrospectionTypeRef) =
            TypeReference.findType t schemaTypes
        let resType = findType opField.Type
        let asyncType = typedefof<Async<obj>>.MakeGenericType(resType)
        let args =
            opField.Args
            |> Seq.map (fun x -> ProvidedParameter(x.Name, findType x.Type))
            |> Seq.toList
        let m = ProvidedMethod(firstToUpper opField.Name, args, asyncType, IsStaticMethod=true)
        let sargs = [ProvidedStaticParameter("content", typeof<string>)]
        m.DefineStaticParameters(sargs, fun methName sargValues ->
            match sargValues with 
            | [| :? string as resFields |] ->
                // This will fail if the query is not well formed
                do Parser.parse resFields |> ignore
                let opField = opField.Name
                let argNames = args |> Seq.map (fun x -> x.Name) |> Seq.toArray
                let m2 = ProvidedMethod(methName, args, asyncType, IsStaticMethod = true)
                m2.InvokeCode <-
                    if resType.Name = "FSharpOption`1" then
                        fun argValues ->
                        <@@
                            (%%makeExprArray argValues: obj[])
                            |> buildQuery opField resFields argNames
                            |> launchRequest serverUrl opName opField Option.ofObj
                        @@>
                    // TODO: It seems there're problems when casting Async<'T> to a primitive type,
                    // we may need to do this for other primitive types. Attempts to isolate the continuation
                    // make it fail when building the expression
                    elif resType.FullName = "System.Boolean" then
                        fun argValues ->
                        <@@
                            (%%makeExprArray argValues: obj[])
                            |> buildQuery opField resFields argNames
                            |> launchRequest serverUrl opName opField unbox<bool>
                        @@>                        
                    else
                        fun argValues ->
                        <@@
                            (%%makeExprArray argValues: obj[])
                            |> buildQuery opField resFields argNames
                            |> launchRequest serverUrl opName opField id
                        @@>
                tdef.AddMember m2
                m2
            | _ -> failwith "unexpected parameter values")
        m.InvokeCode <- fun _ -> <@@ null @@> // Dummy code
        m

    let createMethods (tdef: ProvidedTypeDefinition) (serverUrl: string)
                      (schema: IntrospectionSchema) (schemaTypes: Map<string,TypeReference>)
                      (opType: IntrospectionTypeRef option) =
        match opType with
        | Some op when op.Name.IsSome ->
            let wrapperName, opPrefix =
                if obj.ReferenceEquals(schema.QueryType, opType.Value) then "Queries", "query "
                elif obj.ReferenceEquals(schema.MutationType, opType) then "Mutations", "mutation "
                elif obj.ReferenceEquals(schema.SubscriptionType, opType) then "Subscriptions", "subscription "
                else failwith "Operation doesn't correspond to any operation in the schema"
            let opName = op.Name.Value
            schema.Types
            |> Seq.iter (fun t ->
                if t.Name = opName && t.Fields.IsSome && not (Array.isEmpty t.Fields.Value) then
                    let wrapper = ProvidedTypeDefinition(wrapperName, Some typeof<obj>)
                    t.Fields.Value
                    |> Seq.map (createMethod wrapper schemaTypes serverUrl (opPrefix + opName))
                    |> Seq.toList
                    |> wrapper.AddMembers
                    tdef.AddMember wrapper)
        | _ -> ()

type internal ProviderSchemaConfig =
    { Namespace: string 
      DefinedTypes: Map<string, ProvidedTypeDefinition option> }

[<TypeProvider>]
type GraphQlProvider (config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()
    
    let asm = System.Reflection.Assembly.GetExecutingAssembly()

    do
        let ns = "FSharp.Data.GraphQL"
        let generator = ProvidedTypeDefinition(asm, ns, "GraphQLProvider", Some typeof<obj>)
        generator.DefineStaticParameters([ProvidedStaticParameter("url", typeof<string>)], fun typeName parameterValues ->
            match parameterValues with 
            | [| :? string as serverUrl|] ->
                let choice = Util.requestSchema(serverUrl) |> Async.RunSynchronously
                match choice with
                | Choice1Of2 schema ->
                    let tdef = ProvidedTypeDefinition(asm, ns, typeName, Some typeof<obj>)
                    let schemaTypes =
                        Util.compileTypesFromSchema asm "GraphQLTypes" schema
                    // Inner types
                    let typesWrapper = ProvidedTypeDefinition("Types", Some typeof<obj>)
                    schemaTypes
                    |> Seq.choose (fun kv ->
                        match kv.Value with
                        | ProvidedType(t,_) -> Some t
                        | NativeType _ -> None)
                    |> Seq.toList
                    |> typesWrapper.AddMembers
                    tdef.AddMember typesWrapper
                    // Static methods
                    Util.createMethods tdef serverUrl schema schemaTypes (Some schema.QueryType)
                    Util.createMethods tdef serverUrl schema schemaTypes schema.MutationType
                    Util.createMethods tdef serverUrl schema schemaTypes schema.SubscriptionType
                    // Generic query method
                    let m = ProvidedMethod("Query", [ProvidedParameter("query", typeof<string>)], typeof<Async<obj>>)
                    m.IsStaticMethod <- true
                    m.InvokeCode <- fun argValues ->
                        <@@ launchRequest serverUrl null null id (%%argValues.[0]: string) @@>
                    tdef.AddMember m
                    tdef
                | Choice2Of2 ex -> String.concat "\n" ex |> failwithf "%s"
            | _ -> failwith "unexpected parameter values")
        this.AddNamespace(ns, [generator])

[<assembly:TypeProviderAssembly>]
do ()
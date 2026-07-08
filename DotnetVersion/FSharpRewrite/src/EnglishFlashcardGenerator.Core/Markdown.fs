namespace EnglishFlashcardGenerator.Core

open System
open System.Collections.Generic
open System.Globalization
open System.Text.RegularExpressions
open Markdig
open Markdig.Extensions.Yaml
open Markdig.Syntax
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

[<RequireQualifiedAccess>]
module DateHeadingParser =
    let private formats = [| "dd.MM.yyyy"; "yyyy-MM-dd" |]

    let private tryParseExact (s: string) =
        let mutable value = DateTime.MinValue
        if DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, &value) then
            Some(DateOnly.FromDateTime value)
        else None

    let tryParse (heading: string) =
        let trimmed = heading.Trim()
        let wikiWithAlias = Regex.Match(trimmed, @"^\[\[[^\]|]+\|(?<date>\d{2}\.\d{2}\.\d{4})\]\]$")
        let wikiWithoutAlias = Regex.Match(trimmed, @"^\[\[(?<date>\d{4}-\d{2}-\d{2})(?:-[^\]]+)?\]\]$")
        if wikiWithAlias.Success then tryParseExact wikiWithAlias.Groups.["date"].Value
        elif wikiWithoutAlias.Success then tryParseExact wikiWithoutAlias.Groups.["date"].Value
        else tryParseExact trimmed
        |> Option.map DateHeading.create

[<RequireQualifiedAccess>]
module FrontMatterParser =
    let private serializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()

    let parse (block: YamlFrontMatterBlock option) =
        match block with
        | None -> Map.empty
        | Some yamlBlock ->
            let yaml = yamlBlock.Lines.ToString()
            if String.IsNullOrWhiteSpace yaml then Map.empty
            else
                try
                    let values = serializer.Deserialize<Dictionary<string, obj>>(yaml)
                    if isNull values then Map.empty
                    else
                        values
                        |> Seq.map (fun kv -> kv.Key, if isNull kv.Value then "" else string kv.Value)
                        |> Map.ofSeq
                with _ -> Map.empty

[<RequireQualifiedAccess>]
module MarkdownDocumentParser =
    let private pipeline =
        MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .Build()

    let private headingText (heading: HeadingBlock) =
        if isNull heading.Inline then ""
        else
            heading.Inline
            |> Seq.map string
            |> String.concat ""
            |> fun s -> s.Trim()

    let private clampEnd (text: string) endExclusive =
        Math.Max(0, Math.Min(text.Length, endExclusive))

    let parse (sourcePath: SourcePath option) (text: string) : ParsedDocument =
        let document = Markdown.Parse(text, pipeline)
        let blocks = document |> Seq.cast<MarkdownObject> |> Seq.toList
        let frontMatter =
            blocks
            |> Seq.tryPick (function | :? YamlFrontMatterBlock as y -> Some y | _ -> None)
            |> FrontMatterParser.parse

        let headings : HeadingBlock list =
            blocks
            |> Seq.choose (function | :? HeadingBlock as h -> Some h | _ -> None)
            |> Seq.toList

        let sections : MarkdownSection list =
            headings
            |> List.mapi (fun index (heading: HeadingBlock) ->
                let nextBoundary =
                    headings
                    |> List.skip (index + 1)
                    |> List.tryFind (fun h -> h.Level <= heading.Level)
                    |> Option.map (fun h -> h.Span.Start)
                    |> Option.defaultValue text.Length
                let start = clampEnd text heading.Span.Start
                let stop = clampEnd text nextBoundary
                let rawText = if stop > start then text.Substring(start, stop - start).TrimEnd('\r', '\n') else ""
                let title = headingText heading
                { Level = heading.Level
                  HeadingText = title
                  Date = DateHeadingParser.tryParse title
                  Start = start
                  EndExclusive = stop
                  RawText = rawText })

        { SourcePath = sourcePath
          FrontMatter = frontMatter
          Sections = sections
          OriginalText = text }

[<RequireQualifiedAccess>]
module SectionPlanner =
    let chooseLatestDatedSection (document: ParsedDocument) =
        document.Sections
        |> List.filter (fun s -> s.Level = 2 && s.Date.IsSome)
        |> List.sortByDescending (fun s -> s.Date |> Option.map (DateHeading.value >> _.DayNumber) |> Option.defaultValue 0)
        |> List.tryHead
        |> Option.map (fun section -> { Document = document; Section = section })

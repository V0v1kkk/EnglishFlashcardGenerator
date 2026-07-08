namespace EnglishFlashcardGenerator.Core

open System
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module FakeTeacherAgent =
    let private cardPattern = Regex(@"\*\*(?<front>[^*]+)\*\*\s*-\s*(?<back>[^\r\n]+)(?:\r?\n\*(?<example>[^*]+)\*)?", RegexOptions.Compiled)

    let generate (request: GenerationRequest) =
        let cards =
            cardPattern.Matches(request.Section.RawText)
            |> Seq.cast<Match>
            |> Seq.map (fun m ->
                { Front = m.Groups.["front"].Value.Trim()
                  Back = m.Groups.["back"].Value.Trim()
                  Example =
                    let value = m.Groups.["example"].Value.Trim()
                    if String.IsNullOrWhiteSpace value then None else Some value })
            |> Seq.toList
        { Request = request; Cards = cards }

[<RequireQualifiedAccess>]
module FakeReviewerAgent =
    let review (draft: TeacherDraft) =
        let feedback =
            if draft.Cards.IsEmpty then [ "No flashcards were extracted from the selected section." ]
            else []
        { Draft = draft
          Approved = draft.Cards |> List.isEmpty |> not
          Feedback = feedback
          Iteration = 1 }

[<RequireQualifiedAccess>]
module CardNormalizer =
    let normalize (review: ReviewResult) =
        if not review.Approved then
            { Section = review.Draft.Request.Section; Cards = [] }
        else
            { Section = review.Draft.Request.Section
              Cards =
                review.Draft.Cards
                |> List.distinctBy (fun c -> c.Front.Trim().ToLowerInvariant()) }

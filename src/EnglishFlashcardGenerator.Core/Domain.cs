namespace EnglishFlashcardGenerator.Core;

public sealed record NoteProcessingRequest(
    string SourcePath,
    string CardsOutputDirectory,
    string SourceExcerptOutputDirectory,
    bool Apply,
    int? MaxDays,
    int MaxParallelGroupWorkers,
    int MaxCriticIterations,
    int MaxGroupsPerDay = 4,
    bool PruneSource = false);

public sealed record MarkdownSource(string SourcePath, string Markdown, NoteProcessingRequest Options);
public sealed record DayChunk(int DayIndex, DateOnly? Date, string Heading, string Markdown);
public sealed record DayChunks(MarkdownSource Source, IReadOnlyList<DayChunk> Days, NoteProcessingRequest Options);
public sealed record SelectedDays(MarkdownSource Source, IReadOnlyList<DayChunk> Days, NoteProcessingRequest Options);
public sealed record ProcessedDays(IReadOnlyList<DayResult> Results, NoteProcessingRequest Options);
public sealed record RunSummary(int DaysProcessed, int DaysSucceeded, int DaysFailed, int CardsWritten, IReadOnlyList<string> OutputFiles, IReadOnlyList<string> Warnings);

public sealed record DayProcessingRequest(DayChunk Day, NoteProcessingRequest Options);

public enum TopicGroupKind { Vocabulary, Grammar, Phrase, UsageNote, Mixed }

public sealed record TopicGroup(int GroupIndex, int DayIndex, TopicGroupKind Kind, string Title, string SourceExcerpt, int SourceOrder);
public sealed record GroupPlan(DayChunk Day, IReadOnlyList<TopicGroup> Groups, NoteProcessingRequest Options);
public sealed record ValidatedGroupPlan(DayChunk Day, IReadOnlyList<TopicGroup> Groups, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record WorkerPartitionPlan(DayChunk Day, IReadOnlyList<WorkerBatch> Batches, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record WorkerBatch(int WorkerIndex, IReadOnlyList<TopicGroup> Groups);
public sealed record WorkerBatchRequest(DayChunk Day, WorkerBatch Batch, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record WorkerBatchResult(int WorkerIndex, int ExpectedWorkers, DayChunk Day, NoteProcessingRequest Options, IReadOnlyList<GroupResult> Groups, IReadOnlyList<string> Warnings);
public sealed record MergedGroupResults(DayChunk Day, IReadOnlyList<GroupResult> Groups, IReadOnlyList<Flashcard> Cards, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record DayOutputDraft(DayChunk Day, IReadOnlyList<Flashcard> Cards, string DailySourceExcerptMarkdown, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record DayWritePlan(DayChunk Day, string CardsPath, string CardsMarkdown, int CardsCount, string SourceExcerptPath, string SourceExcerptMarkdown, bool Apply, IReadOnlyList<string> Warnings);
public sealed record DayResult(DayChunk Day, bool Succeeded, int CardsCount, IReadOnlyList<string> OutputFiles, IReadOnlyList<string> Warnings);

public enum CardDirection { OneWay }

public sealed record Card(string Front, string Back, string? Example, CardDirection Direction = CardDirection.OneWay);
public sealed record Flashcard(string Front, string Back, string? Example, CardDirection Direction, int SourceGroupIndex);
public sealed record GroupCardRequest(DayChunk Day, TopicGroup Group, NoteProcessingRequest Options);
public sealed record TeacherRequest(DayChunk Day, TopicGroup Group, int Iteration, string? CriticFeedback, NoteProcessingRequest Options);
public sealed record TeacherDraft(DayChunk Day, TopicGroup Group, int Iteration, IReadOnlyList<Flashcard> Cards, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public enum CriticVerdict { Approved, NeedsRevision, Rejected }
public sealed record CriticReview(DayChunk Day, TopicGroup Group, TeacherDraft Draft, CriticVerdict Verdict, string Feedback, IReadOnlyList<string> Warnings);
public sealed record RevisionDecision(DayChunk Day, TopicGroup Group, TeacherDraft Draft, CriticVerdict Verdict, bool ShouldRevise, bool ShouldFinalize, string Feedback, NoteProcessingRequest Options, IReadOnlyList<string> Warnings);
public sealed record GroupResult(TopicGroup Group, IReadOnlyList<Flashcard> Cards, int Iterations, bool Accepted, IReadOnlyList<string> Warnings);

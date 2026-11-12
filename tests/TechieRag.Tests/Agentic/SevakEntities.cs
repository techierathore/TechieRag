using System.Text.Json;
using TechieRag.Agentic.HostData;

namespace TechieRag.Tests.Agentic;

/// <summary>
/// Fifteen entities with 2 to 9 fields each, shaped like the ones Sevak registers on <see cref="HostDataSkill"/>
/// (tasks, the plan, projects, milestones, goals, areas, people, places, routines, memory, settings and four more):
/// the setup of Sevak's TR-RAG-061 report (REQ-RAG-160). Linked into <c>TechieRag.Local.Tests</c> too.
/// </summary>
internal static class SevakEntities
{
    /// <summary>The number of entities.</summary>
    public const int Count = 15;

    /// <summary>Builds the fifteen entities, every one offering all five operations over an empty store.</summary>
    /// <returns>The entities, in registration order.</returns>
    public static IReadOnlyList<HostEntity> All() =>
    [
        Entity("task", "A to-do item the user wants to get done.",
            Text("title", "What to do, in the user's words.", true), DateTime("due", "When it is due."), Choice("priority", "How urgent it is.", "low", "normal", "high"),
            Text("project", "The project it belongs to, by name."), Text("area", "The area of life it belongs to, by name."), Bool("done", "Whether it is finished."),
            Text("notes", "Anything else the user said about it."), Number("estimate", "How many minutes it should take."), Text("person", "Who it is for or with, by name.")),
        Entity("plan", "One block of time on the user's daily plan.",
            Date("day", "The day of the plan.", true), Text("start", "Start time, HH:mm.", true), Text("end", "End time, HH:mm."), Text("task", "The task done in the block, by title."),
            Choice("kind", "What the block is for.", "focus", "meeting", "break", "errand")),
        Entity("project", "A piece of work with several tasks.",
            Text("name", "The project's name.", true), Text("outcome", "What finished looks like."), Date("deadline", "When it must be done."),
            Choice("status", "Where it stands.", "planned", "active", "paused", "done"), Text("area", "The area of life it belongs to, by name."), Text("goal", "The goal it serves, by name.")),
        Entity("milestone", "A checkpoint inside a project.",
            Text("title", "What is reached.", true), Text("project", "The project, by name.", true), Date("date", "When it should be reached."), Bool("reached", "Whether it is reached.")),
        Entity("goal", "Something the user wants to achieve over months.",
            Text("title", "The goal in the user's words.", true), Text("why", "Why it matters to the user."), Date("by", "When the user wants it."),
            Text("measure", "How the user will know it is met."), Choice("horizon", "How far out it is.", "quarter", "year", "life")),
        Entity("area", "An area of the user's life such as health or work.",
            Text("name", "The area's name.", true), Text("standard", "What keeping it well looks like.")),
        Entity("person", "Someone the user knows.",
            Text("name", "The person's name.", true), Text("relation", "How the user knows them."), Text("phone", "A phone number."), Text("email", "An email address."),
            Date("birthday", "Their birthday."), Text("notes", "Anything the user wants to remember about them."), DateTime("lastContact", "When the user last spoke to them.")),
        Entity("place", "A place the user goes.",
            Text("name", "The place's name.", true), Text("address", "Its address."), Choice("kind", "What kind of place it is.", "home", "work", "shop", "other")),
        Entity("routine", "Something the user does on a schedule.",
            Text("name", "The routine's name.", true), Text("schedule", "When it repeats, in plain words.", true), Text("time", "Time of day, HH:mm."),
            Number("minutes", "How long it takes."), Bool("active", "Whether it is running."), Text("area", "The area of life it belongs to, by name.")),
        Entity("memory", "A fact the assistant should remember about the user.",
            Text("fact", "The fact, in one sentence.", true), Choice("kind", "What kind of fact it is.", "preference", "fact", "instruction")),
        Entity("setting", "One of the app's settings.",
            Text("key", "The setting's name.", true), Text("value", "Its new value.", true)),
        Entity("note", "A free-text note.",
            Text("title", "A short title.", true), Text("body", "The note's text."), Text("tags", "Comma-separated tags."), Text("project", "The project it belongs to, by name.")),
        Entity("habit", "A habit the user tracks every day.",
            Text("name", "The habit's name.", true), Number("target", "How many times a day."), Number("streak", "Days in a row so far."), Bool("doneToday", "Whether it is done today.")),
        Entity("event", "A calendar event.",
            Text("title", "What the event is.", true), DateTime("start", "When it starts.", true), DateTime("end", "When it ends."), Text("place", "Where it is, by name."),
            Text("people", "Who attends, comma-separated names."), Choice("reminder", "When to remind the user.", "none", "10m", "1h", "1d"), Text("notes", "Anything else about it.")),
        Entity("expense", "Money the user spent.",
            Number("amount", "How much, in the user's currency.", true), Date("date", "When it was spent.", true), Text("payee", "Who was paid."),
            Choice("category", "What it was for.", "food", "travel", "home", "health", "fun", "other"), Text("notes", "Anything else about it."))
    ];

    private static HostEntity Entity(string name, string description, params HostEntityField[] fields) =>
        new(name, description, fields)
        {
            List = (_, _) => Task.FromResult<IReadOnlyList<HostEntityRecord>>([]),
            Read = (_, _) => Task.FromResult<HostEntityRecord?>(null),
            Create = (values, _) => Task.FromResult(new HostEntityRecord("1", new Dictionary<string, JsonElement>(values))),
            Update = (id, values, _) => Task.FromResult(new HostEntityRecord(id, new Dictionary<string, JsonElement>(values))),
            Delete = (_, _) => Task.CompletedTask
        };

    private static HostEntityField Text(string name, string description, bool required = false) => new(name, HostFieldType.Text, description, required);

    private static HostEntityField Number(string name, string description, bool required = false) => new(name, HostFieldType.Number, description, required);

    private static HostEntityField Bool(string name, string description) => new(name, HostFieldType.Boolean, description);

    private static HostEntityField Date(string name, string description, bool required = false) => new(name, HostFieldType.Date, description, required);

    private static HostEntityField DateTime(string name, string description, bool required = false) => new(name, HostFieldType.DateTime, description, required);

    private static HostEntityField Choice(string name, string description, params string[] choices) =>
        new(name, HostFieldType.Choice, description) { Choices = choices };
}

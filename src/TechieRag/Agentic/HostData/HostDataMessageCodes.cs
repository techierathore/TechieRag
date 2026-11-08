namespace TechieRag.Agentic.HostData;

/// <summary>
/// Stable codes for what the host-data skill reports to a person, rendered by the host in the user's
/// language (REQ-RAG-153 / BRD-211; REQ-RAG-050). Carried on <c>ToolResult.Message</c> and
/// <see cref="HostDataException.Code"/>.
/// </summary>
public static class HostDataMessageCodes
{
    /// <summary>No entity of that name is registered. <c>{0}</c> entity name.</summary>
    public const string UnknownEntity = "HostDataUnknownEntity";

    /// <summary>The entity does not offer that operation. <c>{0}</c> entity name, <c>{1}</c> operation.</summary>
    public const string OperationNotOffered = "HostDataOperationNotOffered";

    /// <summary>No record has that id. <c>{0}</c> entity name, <c>{1}</c> id.</summary>
    public const string RecordNotFound = "HostDataRecordNotFound";

    /// <summary>The fields the model sent do not fit the entity. <c>{0}</c> entity name, <c>{1}</c> the problems.</summary>
    public const string InvalidFields = "HostDataInvalidFields";

    /// <summary>The model's arguments are not the tool's shape. <c>{0}</c> tool name, <c>{1}</c> the problem.</summary>
    public const string InvalidArguments = "HostDataInvalidArguments";

    /// <summary>The host's handler threw. <c>{0}</c> entity name, <c>{1}</c> operation, <c>{2}</c> exception message.</summary>
    public const string HandlerFailed = "HostDataHandlerFailed";

    /// <summary>A change record cannot be undone as it stands. <c>{0}</c> change id, <c>{1}</c> the reason.</summary>
    public const string CannotUndo = "HostDataCannotUndo";
}

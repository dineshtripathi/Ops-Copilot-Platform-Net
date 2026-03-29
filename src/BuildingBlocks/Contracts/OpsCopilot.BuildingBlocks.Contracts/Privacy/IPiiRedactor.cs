namespace OpsCopilot.BuildingBlocks.Contracts.Privacy;

public interface IPiiRedactor
{
    /// <summary>
    /// Returns a copy of <paramref name="input"/> with recognised PII patterns replaced
    /// by safe placeholder tokens (e.g. [EMAIL], [PHONE], [SSN], [CC], [IP]).
    /// </summary>
    string Redact(string input);
}

namespace Diagnostiq.Core;

/// <summary>Outcome of one check, shared by readiness, storage health, tests and the report.</summary>
public enum CheckState { Pass, Warn, Fail, Unknown }

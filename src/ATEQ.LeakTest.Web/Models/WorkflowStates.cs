namespace ATEQ.LeakTest.Web.Models;

public static class WorkflowStates
{
    public const string Idle = "idle";
    public const string WaitingScan = "waiting_scan";
    public const string Matched = "matched";
    public const string SelectingProgram = "selecting_program";
    public const string Resetting = "resetting";
    public const string Starting = "starting";
    public const string Testing = "testing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Aborted = "aborted";
}

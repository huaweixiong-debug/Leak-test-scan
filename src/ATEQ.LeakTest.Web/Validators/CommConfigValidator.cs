using ATEQ.LeakTest.Web.Models.Dto;
using FluentValidation;

namespace ATEQ.LeakTest.Web.Validators;

public class CommConfigValidator : AbstractValidator<CommConfigRequest>
{
    public CommConfigValidator()
    {
        RuleFor(x => x.ComPort).NotEmpty().WithMessage("COM port is required");
        RuleFor(x => x.Baudrate).GreaterThan(0).WithMessage("baudrate must be a positive integer");
        RuleFor(x => x.DataBits).InclusiveBetween(5, 8).WithMessage("dataBits must be between 5 and 8");
        RuleFor(x => x.Parity).Must(p => new[] { "none", "even", "mark", "odd", "space" }.Contains(p?.ToLower()))
            .WithMessage("parity is invalid");
        RuleFor(x => x.StopBits).InclusiveBetween(1, 2).WithMessage("stopBits must be 1 or 2");
        RuleFor(x => x.TimeoutMs).InclusiveBetween(100, 5000).When(x => x.TimeoutMs.HasValue)
            .WithMessage("timeoutMs must be between 100 and 5000");
        RuleFor(x => x.PollIntervalMs).InclusiveBetween(50, 2000).When(x => x.PollIntervalMs.HasValue)
            .WithMessage("pollIntervalMs must be between 50 and 2000");
        RuleFor(x => x.Enabled).NotNull().WithMessage("enabled must be boolean");
    }
}

public class AteqConfigValidator : CommConfigValidator
{
    public AteqConfigValidator()
    {
        RuleFor(x => x.SlaveId).InclusiveBetween(1, 255).WithMessage("slaveId must be between 1 and 255");
    }
}

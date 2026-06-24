using ATEQ.LeakTest.Web.Models.Dto;
using FluentValidation;

namespace ATEQ.LeakTest.Web.Validators;

public class StartValidator : AbstractValidator<StartRequest>
{
    public StartValidator()
    {
        RuleFor(x => x.StartMode).Must(m => m == null || m == "manual" || m == "scan")
            .WithMessage("startMode must be manual or scan");
    }
}

public class ContextValidator : AbstractValidator<ContextRequest>
{
    public ContextValidator()
    {
        RuleFor(x => x.ProductModel).NotEmpty().WithMessage("productModel is required");
    }
}

public class LineSignalValidator : AbstractValidator<LineSignalRequest>
{
    public LineSignalValidator()
    {
        RuleFor(x => x.Dtr).NotNull().WithMessage("dtr must be boolean");
        RuleFor(x => x.Rts).NotNull().WithMessage("rts must be boolean");
    }
}

public class ProgramTimingValidator : AbstractValidator<int>
{
    public ProgramTimingValidator()
    {
        RuleFor(x => x).InclusiveBetween(1, 255).WithMessage("programNumber must be between 1 and 255");
    }
}

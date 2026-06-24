using ATEQ.LeakTest.Web.Models.Dto;
using FluentValidation;

namespace ATEQ.LeakTest.Web.Validators;

public class ProductProfilesValidator : AbstractValidator<ProductProfilesRequest>
{
    public ProductProfilesValidator()
    {
        RuleFor(x => x.Products).NotEmpty().WithMessage("products must be an array");
        RuleForEach(x => x.Products).ChildRules(product =>
        {
            product.RuleFor(p => p.ProductModel).NotEmpty().WithMessage("productModel is required");
            product.RuleFor(p => p.AteqProgramNo)
                .InclusiveBetween(1, 255).WithMessage("ateqProgramNo must be between 1 and 255");
            product.RuleFor(p => p.QrKeyword).NotEmpty().WithMessage("qrKeyword is required");
        });
    }
}

public class OperatorsValidator : AbstractValidator<OperatorsRequest>
{
    public OperatorsValidator()
    {
        RuleFor(x => x.Operators).NotEmpty().WithMessage("operators must be an array");
        RuleForEach(x => x.Operators).ChildRules(op =>
        {
            op.RuleFor(o => o.Name).NotEmpty().WithMessage("operator name is required");
        });
    }
}

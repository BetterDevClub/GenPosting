using FluentValidation;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.LinkedIn.Validators;

public class LinkedInExchangeTokenRequestValidator : AbstractValidator<LinkedInExchangeTokenRequest>
{
    public LinkedInExchangeTokenRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Authorization code is required.");
    }
}

public class CreateLinkedInPostRequestValidator : AbstractValidator<CreateLinkedInPostRequest>
{
    public CreateLinkedInPostRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Post content is required.")
            .MaximumLength(3000).WithMessage("Post content must not exceed 3000 characters.");

        RuleFor(x => x.ScheduledAt)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("Scheduled time must be in the future.")
            .When(x => x.ScheduledAt.HasValue);
    }
}

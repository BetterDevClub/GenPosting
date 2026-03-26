using FluentValidation;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Scheduling.Validators;

public class UpdateScheduledPostRequestValidator : AbstractValidator<UpdateScheduledPostRequest>
{
    public UpdateScheduledPostRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Post content is required.")
            .MaximumLength(3000).WithMessage("Post content must not exceed 3000 characters.");

        RuleFor(x => x.ScheduledTime)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("Scheduled time must be in the future.");
    }
}

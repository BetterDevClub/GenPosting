using FluentValidation;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Instagram.Validators;

public class InstagramExchangeTokenRequestValidator : AbstractValidator<InstagramExchangeTokenRequest>
{
    public InstagramExchangeTokenRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Authorization code is required.");
    }
}

public class ReplyToCommentRequestValidator : AbstractValidator<ReplyToCommentRequest>
{
    public ReplyToCommentRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Reply message is required.")
            .MaximumLength(2200).WithMessage("Reply message must not exceed 2200 characters.");
    }
}

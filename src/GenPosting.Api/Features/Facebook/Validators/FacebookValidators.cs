using FluentValidation;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Facebook.Validators;

public class FacebookExchangeTokenRequestValidator : AbstractValidator<FacebookExchangeTokenRequest>
{
    public FacebookExchangeTokenRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Authorization code is required.");
        RuleFor(x => x.RedirectUri).NotEmpty().WithMessage("Redirect URI is required.");
    }
}

public class ReplyToFacebookCommentRequestValidator : AbstractValidator<ReplyToFacebookCommentRequest>
{
    public ReplyToFacebookCommentRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Reply message is required.")
            .MaximumLength(8000).WithMessage("Reply message must not exceed 8000 characters.");
    }
}

public class CreateFacebookAlbumRequestValidator : AbstractValidator<CreateFacebookAlbumRequest>
{
    public CreateFacebookAlbumRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Album name is required.");
        RuleFor(x => x.PhotoUrls).NotEmpty().WithMessage("At least one photo URL is required.");
    }
}

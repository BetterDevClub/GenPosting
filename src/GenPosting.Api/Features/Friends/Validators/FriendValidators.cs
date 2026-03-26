using FluentValidation;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Validators;

public class FriendDtoValidator : AbstractValidator<FriendDto>
{
    public FriendDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Friend name is required.");
    }
}

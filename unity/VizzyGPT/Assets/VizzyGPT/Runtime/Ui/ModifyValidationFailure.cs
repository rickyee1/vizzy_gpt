#nullable enable

using System;

namespace VizzyGPT.Runtime.Ui
{
    public sealed class ModifyValidationFailure
    {
        public ModifyValidationFailure(
            string code,
            string? path,
            string message,
            string technicalDetails,
            bool isRepairable)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Path = path;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            TechnicalDetails = technicalDetails ?? throw new ArgumentNullException(nameof(technicalDetails));
            IsRepairable = isRepairable;
        }

        public string Code { get; }
        public string? Path { get; }
        public string Message { get; }
        public string TechnicalDetails { get; }
        public bool IsRepairable { get; }
    }
}

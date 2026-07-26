#nullable enable

using TMPro;

namespace VizzyGPT.Runtime.Ui
{
    public static class CjkTextFontApplicator
    {
        public static void ApplyToInput(TMP_InputField? input, TMP_FontAsset? font)
        {
            if (input == null || font == null)
            {
                return;
            }

            if (input.textComponent != null)
            {
                input.textComponent.font = font;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.font = font;
            }
        }

        public static void ApplyToText(TMP_FontAsset? font, params TMP_Text?[] targets)
        {
            if (font == null)
            {
                return;
            }

            foreach (var target in targets)
            {
                if (target != null)
                {
                    target.font = font;
                }
            }
        }
    }
}

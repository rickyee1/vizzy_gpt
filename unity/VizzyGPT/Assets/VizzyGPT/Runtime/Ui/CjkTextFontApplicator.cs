#nullable enable

using TMPro;

namespace VizzyGPT.Runtime.Ui
{
    public static class CjkTextFontApplicator
    {
        public static bool ApplyToInput(TMP_InputField? input, TMP_FontAsset? font)
        {
            if (input == null || font == null)
            {
                return false;
            }

            var changed = false;
            if (input.textComponent != null)
            {
                changed |= ApplyToTarget(input.textComponent, font);
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                changed |= ApplyToTarget(placeholder, font);
            }

            return changed;
        }

        public static bool ApplyToText(TMP_FontAsset? font, params TMP_Text?[] targets)
        {
            if (font == null)
            {
                return false;
            }

            var changed = false;
            foreach (var target in targets)
            {
                if (target != null)
                {
                    changed |= ApplyToTarget(target, font);
                }
            }

            return changed;
        }

        private static bool ApplyToTarget(TMP_Text target, TMP_FontAsset font)
        {
            var material = font.material;
            if (target.font == font && (material == null || target.fontSharedMaterial == material))
            {
                return false;
            }

            target.font = font;
            if (material != null)
            {
                target.fontSharedMaterial = material;
            }

            target.SetAllDirty();
            return true;
        }
    }
}

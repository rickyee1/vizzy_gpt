using NUnit.Framework;
using VizzyGPT.Core.Security;

namespace VizzyGPT.Core.Tests.Security
{
    public sealed class SecretRedactorTests
    {
        private const string Key = "sk-configured-ABC123";

        [Test]
        public void Redact_replaces_every_exact_occurrence_of_configured_api_key()
        {
            var input = "first=" + Key + "; second=" + Key;

            var result = SecretRedactor.Redact(input, Key);

            Assert.That(result, Is.EqualTo("first=[REDACTED]; second=[REDACTED]"));
            Assert.That(result, Does.Not.Contain(Key));
        }

        [TestCase("Authorization: Bearer bearer-value")]
        [TestCase("authorization: bearer bearer-value")]
        [TestCase("AUTHORIZATION :   BEARER   bearer-value")]
        public void Redact_replaces_authorization_bearer_values_case_insensitively(string input)
        {
            var result = SecretRedactor.Redact(input, configuredApiKey: null);

            Assert.That(result, Does.Contain("[REDACTED]"));
            Assert.That(result, Does.Not.Contain("bearer-value"));
            Assert.That(result, Does.Not.Match("(?i)bearer\\s+bearer"));
        }

        [TestCase("{\"api_key\":\"json-secret\"}")]
        [TestCase("{ \"api_key\"  :  \"json-secret\" }")]
        [TestCase("{\"api\\u005fkey\":\"json-secret\"}")]
        [TestCase("{\"api_key\":\"json-\\\"quoted\\\"-secret\"}")]
        public void Redact_replaces_json_api_key_values_with_spacing_and_escaping(string input)
        {
            var result = SecretRedactor.Redact(input, configuredApiKey: null);

            Assert.That(result, Does.Contain("[REDACTED]"));
            Assert.That(result, Does.Not.Contain("json-secret"));
            Assert.That(result, Does.Not.Contain("quoted"));
        }

        [Test]
        public void Redact_replaces_api_key_inside_an_escaped_json_string()
        {
            const string input = "{\"detail\":\"{\\\"api_key\\\":\\\"nested-secret\\\"}\"}";

            var result = SecretRedactor.Redact(input, configuredApiKey: null);

            Assert.That(result, Does.Contain("[REDACTED]"));
            Assert.That(result, Does.Not.Contain("nested-secret"));
        }

        [Test]
        public void Redact_replaces_html_or_xml_quoted_api_key_value()
        {
            const string input = "<entry>&quot;api_key&quot;: &quot;entity-secret&quot;</entry>";

            var result = SecretRedactor.Redact(input, configuredApiKey: null);

            Assert.That(result, Does.Contain("[REDACTED]"));
            Assert.That(result, Does.Not.Contain("entity-secret"));
        }

        [Test]
        public void Redact_handles_multiple_secret_forms_and_occurrences()
        {
            var input = Key + "\nAuthorization: Bearer bearer-one\n" +
                "{\"api_key\":\"json-one\"}\n" + Key + "\nAuthorization: Bearer bearer-two";

            var result = SecretRedactor.Redact(input, Key);

            Assert.That(Count(result, "[REDACTED]"), Is.EqualTo(5));
            Assert.That(result, Does.Not.Contain(Key));
            Assert.That(result, Does.Not.Contain("bearer-one"));
            Assert.That(result, Does.Not.Contain("bearer-two"));
            Assert.That(result, Does.Not.Contain("json-one"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Redact_treats_null_or_empty_configured_key_as_no_exact_key(string? configuredKey)
        {
            const string input = "ordinary text remains";

            Assert.That(SecretRedactor.Redact(input, configuredKey), Is.EqualTo(input));
        }

        [Test]
        public void Redact_preserves_unrelated_text_exactly()
        {
            const string input = "Status: nominal\nAuthorization was not supplied\n{\"api-key\":\"public-label\"}";

            Assert.That(SecretRedactor.Redact(input, Key), Is.EqualTo(input));
        }

        [Test]
        public void Redact_uses_only_complete_placeholder_and_never_leaks_partial_values()
        {
            const string bearer = "prefix.secret-bearer.suffix";
            const string jsonKey = "prefix.secret-json.suffix";
            var input = "Authorization: Bearer " + bearer + "\n{\"api_key\":\"" + jsonKey + "\"}\n" + Key;

            var result = SecretRedactor.Redact(input, Key);

            Assert.That(result, Does.Not.Contain("prefix"));
            Assert.That(result, Does.Not.Contain("suffix"));
            Assert.That(result, Does.Not.Contain("secret-bearer"));
            Assert.That(result, Does.Not.Contain("secret-json"));
            Assert.That(result, Does.Not.Contain(Key));
            Assert.That(Count(result, "[REDACTED]"), Is.EqualTo(3));
            Assert.That(result.Replace("[REDACTED]", string.Empty), Does.Not.Contain("REDACTED"));
        }

        private static int Count(string value, string fragment)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(fragment, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += fragment.Length;
            }

            return count;
        }
    }
}

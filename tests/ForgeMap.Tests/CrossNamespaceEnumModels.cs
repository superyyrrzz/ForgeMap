// Cross-namespace enum test models for issue #21
// These types MUST be in different namespaces to test auto-enum-cast behavior.

namespace SourceNamespace
{
    public enum AssessmentQuestionKind
    {
        SingleSelect = 0,
        SingleSelectImage = 1,
        MultiSelect = 2
    }

    public class QuestionSource
    {
        public int Id { get; set; }
        public AssessmentQuestionKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class NullableEnumSource
    {
        public int Id { get; set; }
        public AssessmentQuestionKind? Kind { get; set; }
    }
}

namespace DestNamespace
{
    public enum AssessmentQuestionKind
    {
        SingleSelect = 0,
        SingleSelectImage = 1,
        MultiSelect = 2
    }

    public class QuestionDest
    {
        public int Id { get; set; }
        public AssessmentQuestionKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class NullableEnumDest
    {
        public int Id { get; set; }
        public AssessmentQuestionKind? Kind { get; set; }
    }

    public class NonNullableEnumDest
    {
        public int Id { get; set; }
        public AssessmentQuestionKind Kind { get; set; }
    }
}

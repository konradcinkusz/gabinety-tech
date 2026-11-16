using Xunit.Abstractions;
using Xunit.Sdk;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>Marks a concrete contract class as runnable only when the named environment variable is set; its facts are then reported as skipped, not passed.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresEnvAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>A fact that is skipped when the class (or a base class) carries [RequiresEnv] and the variable is unset or empty.</summary>
[XunitTestCaseDiscoverer("Gabinety.Integration.Tests.AuthContract.ContractFactDiscoverer", "Gabinety.Integration.Tests")]
public sealed class ContractFactAttribute : FactAttribute;

public sealed class ContractFactDiscoverer(IMessageSink sink) : FactDiscoverer(sink)
{
    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions options, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new EnvAwareTestCase(DiagnosticMessageSink, options.MethodDisplayOrDefault(), options.MethodDisplayOptionsOrDefault(), testMethod);
}

public sealed class EnvAwareTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", true)]
    public EnvAwareTestCase() { }

    public EnvAwareTestCase(IMessageSink sink, TestMethodDisplay display, TestMethodDisplayOptions options, ITestMethod method)
        : base(sink, display, options, method) { }

    protected override string GetSkipReason(IAttributeInfo factAttribute)
    {
        var type = ((IReflectionTypeInfo)TestMethod.TestClass.Class).Type;
        var required = (RequiresEnvAttribute?)Attribute.GetCustomAttribute(type, typeof(RequiresEnvAttribute), inherit: true);
        if (required is not null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(required.Name)))
            return $"{required.Name} is not set: no real authservice to test against";
        return base.GetSkipReason(factAttribute);
    }
}

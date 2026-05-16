using ExplainMyPC.Intelligence.Models;

namespace ExplainMyPC.Intelligence;

/// <summary>
/// Analyzes a system snapshot and returns a complete set of findings.
///
/// Analyze() is synchronous and fast — all rules are deterministic, in-memory
/// comparisons with no I/O. Callers may invoke it on any thread.
///
/// The interface is intentionally narrow. The ViewModel does not need to know
/// about rules, scorers, or summary generators — only about the output.
/// </summary>
public interface IIntelligenceEngine
{
    EngineOutput Analyze(SystemSnapshot snapshot);
}

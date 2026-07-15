# CLAUDE_REVIEW.md

## Objective

Perform a deep and brutally thorough review of this entire project.

Your goal is to identify:

* Bugs
* Architecture problems
* Performance bottlenecks
* Security vulnerabilities
* Crash causes
* Broken logic
* Bad practices
* Technical debt
* Maintainability issues
* Scalability concerns
* File structure problems
* Dead code
* Missing validation
* Dangerous assumptions
* Race conditions
* Memory leaks
* Dependency risks
* Build/deployment issues
* UX issues
* Accessibility problems
* Anything else that could become a future problem

Do NOT be shallow.
Do NOT just summarize files.
Actually investigate the codebase critically.

---

# Review Requirements

## 1. Code Quality Review

Inspect all code for:

* Poor naming
* Overly complex logic
* Duplicate code
* Massive functions/classes
* Tight coupling
* Low cohesion
* Violations of SOLID principles
* Anti-patterns
* Hidden side effects
* Unsafe mutations
* Bad async handling
* Improper state management
* Magic numbers/strings
* Weak typing
* Missing abstractions
* Premature optimization
* Lack of comments where necessary
* Misleading comments
* Unused variables/imports/files

Highlight:

* What is wrong
* Why it is wrong
* Potential impact
* Suggested fix

---

# 2. Security Audit

Search aggressively for:

* Hardcoded secrets
* API key exposure
* Unsafe environment variable usage
* Injection vulnerabilities
* XSS risks
* CSRF risks
* SQL injection
* Command injection
* Path traversal
* Weak authentication
* Broken authorization
* Insecure token storage
* Session vulnerabilities
* Missing rate limiting
* Missing input sanitization
* Unsafe file uploads
* Open redirects
* Insecure dependencies
* Sensitive logging
* Dangerous regex
* SSRF risks
* Insecure CORS configuration

Check:

* Backend
* Frontend
* APIs
* Infrastructure configs
* CI/CD configs
* Dockerfiles
* GitHub Actions
* Cloud configs

Provide severity levels:

* Critical
* High
* Medium
* Low

---

# 3. Crash & Failure Analysis

Identify anything that could cause:

* App crashes
* Infinite loops
* Deadlocks
* Race conditions
* Memory leaks
* Stack overflows
* Hydration failures
* Rendering crashes
* Null/undefined access
* Promise rejection issues
* Network failure edge cases
* Offline failure handling
* Corrupted state
* Data loss
* Unhandled exceptions

Trace possible crash paths step-by-step.

---

# 4. Performance Review

Inspect for:

* Slow rendering
* Unnecessary re-renders
* N+1 queries
* Blocking operations
* Large bundle sizes
* Memory waste
* Expensive loops
* Unoptimized database queries
* Poor caching
* Missing pagination
* Excessive API calls
* Bad lazy loading
* Missing debouncing/throttling
* Over-fetching
* Under-fetching
* Inefficient algorithms

Estimate likely bottlenecks.

---

# 5. Architecture Review

Analyze:

* Folder structure
* Separation of concerns
* Scalability
* Modularity
* Reusability
* Dependency flow
* Layer boundaries
* State architecture
* API architecture
* Database architecture
* Event flow
* Service boundaries

Identify:

* Fragile areas
* Overengineered areas
* Underengineered areas
* Refactor opportunities

Suggest improved structure where useful.

---

# 6. File Structure Review

Evaluate whether the project structure is:

* Logical
* Maintainable
* Scalable
* Easy to navigate
* Consistent

Identify:

* Misplaced files
* Dead folders
* Confusing naming
* Circular dependencies
* Missing organization
* Inconsistent patterns

Suggest a better structure if needed.

---

# 7. Dependency & Tooling Audit

Review:

* package.json / requirements / Cargo.toml / etc
* Build tooling
* CI/CD setup
* Linting
* Formatting
* Testing setup
* Type checking
* Dev tooling

Check for:

* Deprecated packages
* Unused dependencies
* Vulnerable packages
* Version conflicts
* Missing tooling
* Weak scripts
* Unsafe scripts

---

# 8. Testing Review

Determine:

* Test coverage quality
* Missing edge case tests
* Missing integration tests
* Missing E2E tests
* Brittle tests
* Fake/misleading tests
* Missing failure-path tests

Suggest:

* High priority tests to add
* Critical untested logic

---

# 9. Error Handling Review

Inspect all error handling patterns.

Find:

* Swallowed errors
* Empty catch blocks
* Missing retries
* Weak logging
* User-hostile failures
* Missing fallback UI
* Unsafe assumptions

Ensure:

* Errors are actionable
* Logs are useful
* Failures degrade gracefully

---

# 10. Frontend / UX Review

Inspect for:

* Accessibility issues
* Keyboard navigation problems
* Color contrast issues
* Mobile responsiveness
* Layout instability
* Confusing UX
* Bad loading states
* Missing empty states
* Poor form validation
* Weak error messaging

---

# 11. Backend Review

Inspect:

* API design
* Validation
* Authentication
* Authorization
* Database handling
* Queue handling
* Background jobs
* Caching
* Scalability
* Retry behavior
* Logging
* Monitoring readiness

---

# 12. Database Review

Inspect for:

* Missing indexes
* Bad schema design
* Data consistency risks
* Migration dangers
* Cascade delete risks
* Missing constraints
* Transaction safety issues
* Scalability concerns

---

# 13. DevOps & Deployment Review

Review:

* Docker setup
* Environment management
* CI/CD pipelines
* Infrastructure configs
* Secrets handling
* Production readiness
* Monitoring/logging
* Backup strategy
* Rollback strategy

Identify production risks.

---

# 14. Documentation Review

Check whether documentation is:

* Accurate
* Complete
* Up to date
* Helpful for onboarding
* Helpful for deployment
* Helpful for debugging

Identify missing documentation.

---

# 15. Priority Ranking

At the end, produce:

## Critical Issues

Must fix immediately.

## High Priority Issues

Should fix soon.

## Medium Priority Issues

Important but not urgent.

## Low Priority Issues

Nice improvements.

---

# 16. Refactor Suggestions

Suggest:

* High impact refactors
* Simplifications
* Architecture improvements
* Performance improvements
* Security improvements

Include estimated impact.

---

# 17. Final Verdict

Provide:

* Overall codebase health score (1-10)
* Security score
* Scalability score
* Maintainability score
* Production readiness score

Then explain:

* Biggest strengths
* Biggest weaknesses
* Most dangerous risks
* What should be prioritized first

---

# Output Format

For every issue include:

* Title
* Severity
* Location
* Explanation
* Impact
* Suggested Fix

Example:

## Unsafe Null Access in User Profile Loader

Severity: High
Location: src/profile/loadUser.ts:48

Explanation:
Potential undefined access when API response fails.

Impact:
Can crash the application during login.

Suggested Fix:
Add response validation and fallback handling before property access.

---

# Important Instructions

* Be skeptical.
* Assume bugs exist.
* Look for hidden risks.
* Do not avoid criticism.
* Do not give generic praise.
* Focus on actionable findings.
* Prefer depth over breadth.
* Trace execution paths carefully.
* Investigate edge cases.
* Think like a senior engineer, security auditor, QA engineer, and production SRE combined.

If uncertain about something, explicitly label it as:

* Confirmed Issue
* Likely Issue
* Possible Risk

Do not fabricate findings.

## Summary

<!-- What does this PR change, and why? -->

## Related issue

<!-- Closes #123, if applicable -->

## Checklist

- [ ] I followed the existing code style and patterns in neighboring files.
- [ ] I added/updated XML doc comments for public API changes.
- [ ] I added tests covering this change (or explained why none are needed).
- [ ] I updated `README.md` if public API or behavior changed.
- [ ] `dotnet test YeelightClient.Tests/YeelightClient.Tests.csproj` passes locally (any OS). On Windows, `dotnet build YeelightClient.sln` also builds the WinUI 3 test app (`YeelightClient.TestApp`); it cannot be built on Linux or macOS.
- [ ] No test or tool of this change sends anything to a real light: tests run against the simulated light (`SimulatedBulb`) on the loopback.

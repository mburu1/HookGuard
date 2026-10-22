<!-- Milestone: M?.? -->
## Summary

<!-- What does this change and why. One paragraph. -->

## Test evidence

<!-- Paste the commands you ran and their outcome. Screenshots for UI changes. -->

```
dotnet test HookGuard.slnx
```

## Risks and trade-offs

<!-- Anything a reviewer should push back on. Call out what you deliberately did not do. -->

## Checklist

- [ ] CI is green (build, tests, `dotnet format --verify-no-changes`)
- [ ] New behaviour is covered by a test
- [ ] Domain and Application have no outward dependencies (architecture test passes)
- [ ] No secrets, tokens or real payloads committed
- [ ] README / docs / ADR updated when a decision changed
- [ ] Milestone checkbox in the README roadmap ticked

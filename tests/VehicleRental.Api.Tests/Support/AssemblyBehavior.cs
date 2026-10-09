// Every test here starts a whole application host (WebApplicationFactory). Starting several hosts at the same moment in
// one process can make them interfere (minimal-hosting startup attaches to process-wide diagnostic listeners), which showed
// up once on a hosted runner as "Cannot access a disposed object" in a startup-failure test that passes in isolation.
// Starting hosts one at a time removes that class of flake; the non-database tests are fast, so the cost is small.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

# Publication checklist

The steps used to publish this repository to GitHub safely, kept as a reference for re-checking before any future change of visibility or history.

## 1. Before creating anything

- [ ] The working tree is clean (`git status`) and you are on `main`.
- [ ] Run the hygiene script: `bash scripts/ci/check-repo-hygiene.sh`.
- [ ] Re-scan the tracked files **and the whole history** for anything personal or secret. The full history becomes public. Search for your student number, student email address, university and unit names, absolute local paths, passwords, signing keys and tokens, for example:
  ```bash
  git grep -n -i -E "<term>" $(git rev-list --all)
  git log --all --format='%an <%ae> | %cn <%ce>' | sort -u
  ```
- [ ] Confirm every commit author and committer is the GitHub noreply address.
- [ ] Check the largest tracked files: `git ls-files -z | xargs -0 du -k | sort -rn | head`.
- [ ] Run the full local verification: build with `-warnaserror`, all tests with PostgreSQL available, and the Docker smoke test.
- [ ] If any scan finds something in an old commit, stop. Decide deliberately whether to rewrite history before publishing, because publication cannot be undone.

## 2. Create the repository

- [ ] Create an **empty** repository on GitHub. Do not add a README, license or `.gitignore` there; they already exist locally.
- [ ] Use the suggested name, description and topics at the end of this page.
- [ ] Add the remote and check it: `git remote add origin <repository URL>`, then `git remote -v`.
- [ ] Push once: `git push -u origin main`.

## 3. Watch the first CI run

- [ ] Open the Actions tab and watch the first run. The workflow has never run on GitHub-hosted runners, so expect to fix something runner-specific.
- [ ] Confirm all three jobs pass, the test-results artifact uploads, and the test summary shows every test passed and none skipped.
- [ ] Fix any problems in small commits.

## 4. Repository settings

- [ ] Enable the dependency graph, Dependabot alerts and Dependabot security updates (version updates are already configured in `.github/dependabot.yml`).
- [ ] Enable secret scanning and push protection if available.
- [ ] Add a branch protection rule or ruleset for `main` that requires the CI jobs to pass before merging.
- [ ] Under Actions settings, keep the default workflow token permission read-only.
- [ ] Add the description and topics.
- [ ] Optionally add a social preview image: a simple, clean card with the project name and no personal data.

## 5. Finish the README

- [ ] Add the CI status badge **only now**, after the workflow exists on GitHub:
  `![CI](https://github.com/<owner>/<repo>/actions/workflows/ci.yml/badge.svg)`
- [ ] Update the README's CI status paragraph, the `docs/ci.md` limitations and the architecture overview to say the workflow has run.
- [ ] Add real screenshots (see [screenshots](screenshots.md)) if you want them.
- [ ] Pin the repository on your GitHub profile.

## Recommended metadata

- **Name:** `vehicle-rental-management-system`
- **Description:** Production-style vehicle rental backend built with ASP.NET Core, PostgreSQL, JWT authentication, Docker and automated testing.
- **Topics:** `dotnet`, `csharp`, `aspnet-core`, `postgresql`, `entity-framework-core`, `rest-api`, `jwt`, `docker`, `clean-architecture`, `xunit`

# Security

Lamha opens files that may come from anywhere, so problems in how it handles them matter.

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's private vulnerability reporting instead: on the repository's
**Security** tab, choose **Report a vulnerability**. Include the Lamha version, the Windows version, and if possible a
file that reproduces the problem.

Examples of what counts:

- a file that makes Lamha reach the network, run code, or run a macro or script;
- a file that makes Lamha write to, rename or delete anything other than its own settings, logs and cache, or the
  split and merge results it is asked to save;
- a crash or hang caused by a crafted file;
- Space being taken away from a program other than File Explorer.

## Design safeguards

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) describes them: files are opened read-only, Office files are converted from a
temporary copy with macros and link updates disabled, SVG is stripped of external references, and nothing in the app
uses the network.

# Operations

This section groups packaging and release concerns for `TplQueue.Core`.

## Strong-name signing

Normal source builds are unsigned.

Official signed release packages are produced only when `pack-local.ps1` receives:

- an external private `.snk` path
- the matching full public key

For coordinated public release validation and publication, use the workspace scripts instead of signing this repository in isolation.

## Release flow

The public release flow is coordinated from `WorkspaceTplQueue`:

```powershell
.\pack.ps1 -Version <version> -StrongNameKeyFile <private-key-path> -StrongNamePublicKey <public-key>
.\publish.ps1 -Version <version> -ExpectedStrongNamePublicKey <public-key>
```

The active public preview line is `0.1.0-preview.1`.

## License and source access

`TplQueue.Core` is distributed under a custom binary-use EULA.

Official binaries may be consumed under the package license terms, while source access and rights around modified-source distribution are governed separately.

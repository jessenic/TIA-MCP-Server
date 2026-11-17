# Third-party notices

## siemens-docs-mcp

The Siemens online documentation tools (`src/Docs/`, tools `docs_search`,
`docs_read_page`, `docs_get_toc` and `docs_list_publications`) are a C# port of
[siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp), used under the
MIT License:

```
MIT License

Copyright (c) 2026 Łukasz Czarnacki

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## HtmlAgilityPack

The documentation tools parse HTML with
[HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack)
(NuGet package, MIT License, Copyright (c) ZZZ Projects Inc.).

## DotNetProjects.S7CommPlusDriver

The online PLC tools (`src/PlcOnline/`, tools `plc_online_*`) use the
[DotNetProjects.S7CommPlusDriver](https://github.com/dotnetprojects/S7CommPlusDriver)
NuGet package (based on [thomas-v2/S7CommPlusDriver](https://github.com/thomas-v2/S7CommPlusDriver)),
Copyright (c) 2025, DotNetProjects, Thomas Wiens, licensed under the
[GNU Lesser General Public License v3.0 or later](https://www.gnu.org/licenses/lgpl-3.0.html).
It is used unmodified as a separate library (`S7CommPlusDriver.dll`), which may be replaced
with another compatible build. Its source code is available from the repository above.

The package depends on, and the server ships:

- [HarpoS7](https://github.com/bonk-dev/HarpoS7) and HarpoS7.PublicKeys (MIT License,
  Copyright (c) 2024 bonk): S7CommPlus legacy challenge authentication.
- [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) (MIT License,
  Copyright (c) The Legion of the Bouncy Castle Inc.): TLS 1.3.
- ZlibCore (`zlib.net.dll`, BSD-style licence, Copyright (c) ComponentAce and JCraft, Inc.).
- Microsoft.Extensions.Logging.Abstractions (MIT License, Copyright (c) .NET Foundation).
- OpenSSL 3 native libraries (`libssl-3-x64.dll`, `libcrypto-3-x64.dll`, Apache License 2.0),
  copied by the package for its optional OpenSSL TLS backend; this server uses the BouncyCastle backend.

# Contribuindo

Obrigado pelo interesse no Orla. Toda participação está sujeita ao [Código de Conduta](CODE_OF_CONDUCT.md).

## Escopo

Mantenha a proposta do projeto: uma interface pequena, rápida e reversível, sem serviços, WebView residente, injeção em outros processos ou alteração de arquivos e políticas do Windows. Prefira APIs públicas ou documentadas e preserve o comportamento nativo dos atalhos `Win+...`.

Antes de uma mudança ampla de interface ou arquitetura, abra uma discussão para validar o encaixe no projeto.

## Preparação

1. Use Windows 10/11 x64, PowerShell 5.1+ e .NET Framework 4.8.
2. Crie um fork e uma branch curta a partir de `main`.
3. Faça mudanças pequenas e com uma finalidade clara.
4. Não inclua caminhos de usuário, nomes de rede, empresa, tokens, logs, `settings.ini` ou binários não reproduzíveis.

## Verificação

Execute antes de enviar:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Verify-Repository.ps1
```

O comando compila em uma pasta temporária, valida a versão, os SVGs, os links locais e o diff. Siga também a [matriz funcional](docs/TESTING.md), especialmente clique/minimização, Teams/WebView, atalhos nativos, restauração da taskbar e dois monitores quando a mudança alcançar essas áreas.

## Pull request

Explique no pull request:

- o problema e o comportamento esperado;
- o que mudou e por quê;
- como foi testado, incluindo monitores e DPI quando relevante;
- impacto observado em CPU e memória para mudanças residentes;
- capturas sem informações pessoais para alterações visuais.

Não misture refatorações não relacionadas. Um mantenedor pode pedir ajustes antes do merge mesmo quando o build estiver verde.

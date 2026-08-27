<div align="center">
  <img src="docs/images/orla-mark.svg" width="92" alt="Símbolo do Orla">
  <h1>Orla</h1>
  <p><strong>Uma borda leve e elegante para o Windows.</strong></p>
  <p>Topbar e dock multi-monitor, sem WebView, widgets residentes ou modificação de arquivos do sistema.</p>

  [![Build](https://github.com/Andradev/orla-windows/actions/workflows/build.yml/badge.svg)](https://github.com/Andradev/orla-windows/actions/workflows/build.yml)
  [![Release](https://img.shields.io/github/v/release/Andradev/orla-windows?display_name=tag&style=flat)](https://github.com/Andradev/orla-windows/releases)
  [![License](https://img.shields.io/badge/license-MIT-0A84FF.svg)](LICENSE)
  ![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0A84FF.svg)
</div>

![Orla em dois monitores](docs/images/orla-hero.svg)

## Por que Orla?

Orla substitui somente a experiência visual das bordas do desktop: uma topbar discreta e um dock flutuante. O Explorer continua sendo o shell do Windows, os atalhos `Win+...` permanecem nativos e nenhuma política corporativa é alterada.

O projeto nasceu de uma meta simples: manter a fluidez visual de um dock moderno usando um único processo pequeno, auditável e reversível.

## Recursos

- uma topbar e um dock independentes em cada monitor;
- topbar registrada como AppBar, reservando espaço para não cobrir janelas maximizadas;
- dock com ocultação automática e revelação em toda a borda inferior;
- aplicativos abertos aparecem nos dois docks e janelas do mesmo app são agrupadas;
- clique no app ativo minimiza e devolve o foco ao aplicativo anterior; outro clique restaura; clicar em outro app o ativa;
- indicador de foco atualizado imediatamente por evento nativo do Windows;
- ordem dos aplicativos por arrastar e soltar, sem transformar apps fechados em fixos;
- somente o Explorador fica fixado por padrão;
- menu de contexto para janelas, nova instância, fixar, desafixar e fechar;
- Fluent Search opcional: `Win` isolado abre a busca pelo canal local da própria instância, já posicionada no monitor em uso e sem salto visível;
- botões do Fluent Search em cada tela sempre abrem a busca naquela tela;
- `Win+Shift+S`, `Win+E`, `Win+D` e outras combinações continuam no Windows;
- restauração explícita da barra nativa ao sair, desinstalar ou usar `--restore`.

## Desempenho medido

Teste contínuo de 10 minutos em Windows 11, Intel Iris Xe e dois monitores:

| Medida | Orla | Seelen UI no mesmo PC |
|---|---:|---:|
| CPU média | **0,458%** | 1,144% |
| Working set médio | **78,08 MiB** | 868,46 MiB |
| Working set máximo | **80,05 MiB** | 1.221,24 MiB |
| Memória privada média | **64,60 MiB** | 389,65 MiB |
| Processos | **1** | 10–15 |
| Erros/avisos durante o teste | **0** | 8 |

![Comparação de desempenho](docs/images/performance.svg)

Os resultados variam conforme GPU, escala de DPI, quantidade de monitores e aplicativos abertos. Os números acima são evidência de uma máquina real, não uma garantia universal.

## Instalação

Requisitos: Windows 10/11 x64, PowerShell 5.1+ e .NET Framework 4.8. Fluent Search é opcional.

```powershell
git clone https://github.com/Andradev/orla-windows.git
cd orla-windows
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

O instalador compila o código localmente, instala em `%LOCALAPPDATA%\Orla` e cria somente a entrada de inicialização do usuário. Não solicita privilégios de administrador.

Para não iniciar automaticamente no login:

```powershell
.\Install.ps1 -NoStartup
```

Instalações antigas do VictorShell são migradas automaticamente: configurações são preservadas e a entrada antiga de inicialização é removida.

## Uso

| Ação | Resultado |
|---|---|
| Clique | Ativa; se já estiver ativo, minimiza |
| Clique do meio | Abre uma nova janela/instância |
| Arrastar | Reorganiza o aplicativo nos dois docks |
| Botão direito | Mostra ações e janelas do grupo |
| Borda inferior | Revela o dock daquele monitor |
| `Win` isolado | Abre o Fluent Search no monitor do cursor |
| Botão de busca | Abre o Fluent Search no monitor do botão |

## Configuração

Na primeira execução, Orla cria `%LOCALAPPDATA%\Orla\settings.ini`:

```ini
FluentSearchPath=C:\Program Files\Fluent Search\FluentSearch.exe
BareWindowsKeyOpensFluent=true
TopBarHeight=29
DockReservedHeight=61
PinnedApp=Explorador de Arquivos|C:\Windows\explorer.exe
```

Feche o Orla antes de editar. Use `BareWindowsKeyOpensFluent=false` para manter o menu Iniciar na tecla Windows. Linhas `AppOrder=` guardam somente a ordem; aplicativos fechados continuam desaparecendo.

## Desinstalação e recuperação

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

Para preservar configuração e log:

```powershell
.\Uninstall.ps1 -KeepSettings
```

Recuperação direta da barra nativa:

```powershell
%LOCALAPPDATA%\Orla\Orla.exe --restore
```

## Arquitetura

![Arquitetura do Orla](docs/images/architecture.svg)

Orla é um executável WPF/.NET Framework x64 sem pacotes externos. Ele combina AppBar, enumeração de janelas, `SetWinEventHook` para foco e um hook de teclado limitado ao `Win` isolado. Veja [Arquitetura](docs/ARCHITECTURE.md) e [Solução de problemas](docs/TROUBLESHOOTING.md).

## Build

```powershell
.\Build.ps1
```

O resultado fica em `dist\Orla.exe`. O binário da release é intencionalmente não assinado; confira o SHA-256 publicado ou compile localmente.

## Limitações

- estados de rede, som e bateria são indicadores simples, não flyouts personalizados;
- janelas elevadas podem recusar ativação por um processo não elevado;
- Fluent Search é um projeto separado e não é instalado automaticamente;
- a identidade visual é inspirada em princípios gerais de clareza, hierarquia e movimento, sem afiliação com fabricantes de sistemas operacionais.

## Projeto

- [Contribuindo](CONTRIBUTING.md)
- [Segurança](SECURITY.md)
- [Licença MIT](LICENSE)
- [Releases](https://github.com/Andradev/orla-windows/releases)

Orla é independente e não é afiliado à Apple, Microsoft, Seelen UI ou Fluent Search.

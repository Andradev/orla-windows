# VictorShell

Uma topbar e um dock leves, minimalistas e multi-monitor para Windows 10/11, inspirados no comportamento do macOS e na experiência do Seelen UI — sem WebView, widgets ou gerenciador de janelas.

> Estado: versão inicial funcional, criada e testada em Windows 11 com dois monitores.

## Destaques

- topbar reservada em cada monitor, sem cobrir janelas maximizadas;
- dock flutuante em cada monitor, com ocultação automática por sobreposição;
- encostar o mouse em qualquer ponto da borda inferior revela o dock daquele monitor;
- aplicativos abertos aparecem nos dois docks e janelas do mesmo app são agrupadas;
- clique no app ativo minimiza; novo clique restaura; clique em outro app o ativa;
- indicador de foco atualizado por evento nativo do Windows;
- arrastar reorganiza os aplicativos nos dois monitores sem fixá-los;
- somente o Explorador fica fixado por padrão; apps temporários somem ao fechar;
- menu de contexto com janelas abertas, nova janela, fixar/desafixar e fechar;
- integração opcional com [Fluent Search](https://fluentsearch.net/): `Win` sozinho abre a busca, mas combinações como `Win+Shift+S`, `Win+E` e `Win+D` continuam no Windows;
- barra nativa é restaurada ao sair ou executar `VictorShell.exe --restore`;
- instalação no perfil do usuário; não modifica arquivos do Windows nem políticas.

## Requisitos

- Windows 10 ou Windows 11 x64;
- .NET Framework 4.8 (incluído nas versões atuais do Windows);
- PowerShell 5.1 ou posterior;
- Fluent Search é opcional. Sem ele, a tecla Windows nativa é preservada.

## Instalação

Abra PowerShell na pasta clonada e execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

O script compila localmente, copia o executável para `%LOCALAPPDATA%\VictorShell` e cria a entrada de inicialização do usuário. Nenhum privilégio de administrador é necessário.

Para instalar sem iniciar automaticamente no login:

```powershell
.\Install.ps1 -NoStartup
```

## Uso

- Clique: ativa o app; se já estiver ativo, minimiza.
- Clique do meio: abre uma nova janela/instância.
- Arrastar: muda a ordem; isso não fixa o app.
- Botão direito: janelas do grupo, abrir nova, fixar/desafixar e fechar.
- Borda inferior: mostra o dock em qualquer ponto da largura do monitor.
- Botão direito no fundo da topbar/dock: restaura a barra do Windows e sai.

## Configuração

Na primeira execução é criado `%LOCALAPPDATA%\VictorShell\settings.ini`:

```ini
FluentSearchPath=C:\Program Files\Fluent Search\FluentSearch.exe
BareWindowsKeyOpensFluent=true
TopBarHeight=29
DockReservedHeight=61
FluentSearchHotkey=162,164,32
PinnedApp=Explorador de Arquivos|C:\Windows\explorer.exe
```

Feche o VictorShell antes de editar. Defina `BareWindowsKeyOpensFluent=false` para manter o menu Iniciar na tecla Windows. A ordem arrastada é gravada em `AppOrder=`; ela não mantém aplicativos fechados visíveis.

## Desinstalação e recuperação

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

Para preservar `settings.ini` e o log:

```powershell
.\Uninstall.ps1 -KeepSettings
```

Em qualquer situação, também é possível executar:

```powershell
%LOCALAPPDATA%\VictorShell\VictorShell.exe --restore
```

## Desempenho de referência

Em um notebook Windows 11 com Intel Iris Xe e dois monitores, uma medição de 10 minutos registrou:

- CPU média: **0,458%**;
- working set médio: **78,08 MiB**;
- working set máximo: **80,05 MiB**;
- memória privada média: **64,60 MiB**;
- variação de memória privada: **3,59 MiB**;
- um único processo, responsivo em todas as 112 amostras e sem erros registrados.

Resultados variam conforme GPU, quantidade de monitores, aplicativos abertos e escala de DPI.

## Build e desenvolvimento

```powershell
.\Build.ps1
```

O executável é gerado em `dist\VictorShell.exe`. O projeto usa WPF e APIs Win32 diretamente para continuar pequeno e evitar dependências adicionais.

Veja [Arquitetura](docs/ARCHITECTURE.md), [Solução de problemas](docs/TROUBLESHOOTING.md), [Contribuindo](CONTRIBUTING.md) e [Segurança](SECURITY.md).

## Limitações conhecidas

- o binário compilado localmente não possui assinatura Authenticode;
- a topbar usa estado simples de rede/volume/bateria, não flyouts personalizados;
- apps elevados podem impedir ativação a partir de um processo não elevado, por proteção do Windows;
- o Fluent Search é um projeto separado e não é instalado automaticamente.

## Créditos e referências

- [Apple — Desktop & Dock settings](https://support.apple.com/guide/mac-help/change-desktop-dock-settings-mchlp1119/26/mac/26)
- [Apple — Dock menus HIG](https://developer.apple.com/design/human-interface-guidelines/dock-menus)
- [Microsoft — Customize the taskbar](https://support.microsoft.com/en-US/Windows/Experience/Personalization/customize-the-taskbar-in-windows)
- [Seelen UI](https://github.com/eythaann/seelen-ui)
- [Fluent Search](https://github.com/adirh3/Fluent-Search)

VictorShell é independente e não é afiliado à Apple, Microsoft, Seelen UI ou Fluent Search.

## Licença

[MIT](LICENSE)

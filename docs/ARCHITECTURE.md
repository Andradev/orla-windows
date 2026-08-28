# Arquitetura

Orla é um único executável WPF/.NET Framework x64, sem pacotes externos, serviço residente, injeção em processos ou WebView.

## Componentes

- `ShellController`: ciclo de vida, monitores, Fluent Search e tecla Windows.
- `TopBarWindow`: AppBar Win32 registrada em cada monitor para reservar a área superior.
- `DockWindow`: dock flutuante, auto-hide, agrupamento, clique, arrasto e indicadores.
- `WindowCatalog`: enumeração de janelas e metadados de processos.
- `ForegroundWindowTracker`: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` para foco sem polling rápido.
- `BareWindowsKeyHook`: suprime somente `Win` isolado e reinsere `Win` quando outra tecla forma uma combinação.
- `TaskbarController`: oculta e restaura as taskbars nativas de forma reversível.
- `ShellSettings`: INI simples no perfil do usuário, com migração da instalação anterior.
- `SystemStatusMonitor`: uma fonte compartilhada de rede, áudio e bateria para as topbars, além de Bluetooth e ações rápidas para o painel.
- `QuickPanelWindow`: flyout transitório que reutiliza o monitor de status compartilhado, criado somente quando aberto e totalmente descartado ao fechar.
- `BrightnessService`: controle transitório de brilho por WMI na tela integrada e DDC/CI nos monitores externos compatíveis.
- `RadioService`: leitura e alternância de Wi-Fi/Bluetooth por `Windows.Devices.Radios`, carregada dinamicamente para preservar o executável único.
- `Loc`: seleciona português, inglês ou espanhol pelo idioma de exibição do Windows, com fallback em inglês; data e hora usam independentemente o formato regional do usuário.

## Inicialização do usuário

O instalador registra `%LOCALAPPDATA%\Orla\Orla.exe --startup` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Assim que o Explorer inicia a sessão, a Orla lê as preferências persistidas, cria uma topbar e um dock para cada monitor, oculta as barras nativas e reinstala a integração da tecla Windows.

Antes de adquirir o mutex de instância única, uma cópia executada fora da pasta oficial transfere o início para o executável instalado. Isso cobre o recurso do Windows que reabre aplicativos da sessão anterior e evita que binários baixados ou temporários assumam o desktop. `--portable` desativa explicitamente esse redirecionamento para desenvolvimento e testes.

## Fluent Search multi-monitor

Cada botão informa o `Screen.DeviceName` de sua própria tela; a tecla Windows usa a tela do cursor. Antes de enviar `RequestOpen` pelo pipe local, Orla transfere o HWND ainda oculto para a área útil solicitada. Depois que o Fluent o torna visível, Orla não altera mais a posição: o próprio Fluent controla a transição entre a altura temporária e a barra compacta, sem um segundo salto vertical.

Os pedidos são serializados em vez de descartados. Se a busca está visível, o mesmo comando a oculta e a espera termina assim que esse estado é confirmado; um novo toque pode reabri-la imediatamente.

Essa ordem é importante: reexibir diretamente o HWND oculto produz uma janela visualmente aberta, mas sem o estado interno de pesquisa. Orla não simula o hotkey, não força uma janela oculta a aparecer e não reinicia à força o Fluent Search.

## Desempenho

Topbars opacas e docks transparentes usam renderização por software para manter previsível a memória em GPUs integradas. Foco, disponibilidade de rede e volume usam eventos, sem polling rápido. Bateria, Bluetooth, intensidade do Wi-Fi e os estados das ações rápidas são consultados a cada 10 segundos por uma única instância compartilhada, também reutilizada pelo painel. A descoberta WMI/DDC-CI ocorre em uma thread de trabalho somente enquanto o painel está aberto e os movimentos do slider são agrupados antes da escrita. O catálogo do dock roda a cada 2 segundos; borda e sobreposição usam cache.

O áudio usa `IAudioEndpointVolume` e seu callback nativo. A rede usa `NetworkChange`; quando a interface ativa é Wi-Fi, o RSSI vem de `WlanQueryInterface` e é convertido em uma qualidade de 0–100%. A conexão atual também fornece o nome do perfil quando o Windows permite; uma restrição de privacidade ou política apenas remove esse nome e preserva o indicador de sinal. Não há enumeração de perfis salvos nem solicitação de permissão. Bluetooth é enumerado pelas APIs Win32 e o respectivo handle sempre é fechado na mesma leitura.

O painel usa uma grade 2×2 de ações compactas, ícones em caixas ópticas de 30×30 e duas linhas separadas apenas para ícone + slider de volume e brilho. Wi-Fi e Bluetooth são split buttons: o corpo alterna `Radio.State` e o chevron abre a URI `ms-settings:` correspondente. O nome da conexão ou do dispositivo aparece truncado quando disponível. Cada operação é serializada, tem timeout de cinco segundos e atualiza a superfície azul somente após reler o estado real. Economia de energia e Luz noturna repetem a mesma divisão e o mesmo hover, sinalizam ligado/desligado e abrem suas URIs oficiais porque não há API pública de escrita equivalente. O primeiro estado vem de `Windows.System.Power.PowerManager`; a Luz noturna usa leitura passiva do estado CloudStore conhecido e assume “indisponível” se o formato não for reconhecido. Nenhum desses dados internos é escrito. O ícone de volume alterna mute sem botão textual duplicado; tanto o ícone quanto o slider expõem a porcentagem atual no hover. O brilho integrado usa `WmiMonitorBrightnessMethods`; monitores externos são enumerados por `GetPhysicalMonitorsFromHMONITOR`, lidos por `GetMonitorBrightness` e atualizados por `SetMonitorBrightness`. Topbar e painel compartilham os vetores oficiais Microsoft Fluent UI System Icons na grade nativa de 20×20; a bateria usa dez níveis e um estado próprio de carregamento. A topbar apresenta Wi-Fi, volume e bateria em um único botão com `ControlFillColor` no hover/pressionamento; Bluetooth fica apenas no painel. O conteúdo permanece imóvel no hover e usa escala somente durante o clique. Na abertura dos ícones ocultos, um `SetWinEventHook` transitório acompanha criação, exibição e posição do HWND nativo; uma região de desenho vazia bloqueia qualquer quadro na posição inferior e é restaurada somente depois de `SetWindowPos` junto à topbar. O segundo clique fecha o flyout e a rotação do chevron é sincronizada entre as topbars. O dock não participa dessas ações.

O dock mantém o histórico dos dois últimos aplicativos externos, ignorando suas próprias janelas e janelas auxiliares do mesmo processo. Ao minimizar o aplicativo ativo, Orla promove o aplicativo anterior e sincroniza imediatamente os indicadores dos dois monitores. Antes de ativar um app, resolve novamente seus HWNDs para não usar uma janela substituída desde o último refresh; uma recusa temporária de foco do Windows recebe tentativas curtas e limitadas. A Lixeira vem do catálogo de ícones stock do Shell e `SHQueryRecycleBin` atualiza seu estado sem criar outro processo residente. Seu botão conserva a mesma animação de flutuação dos demais itens do dock.

## Multi-monitor

Cada `Screen.DeviceName` recebe uma topbar e um dock. A lista e a ordem são compartilhadas; posicionamento, área útil, auto-hide e destino do Fluent Search pertencem a cada tela.

## Reversibilidade

Appbars existem apenas durante o processo. Na saída, Orla remove os registros, mostra as taskbars nativas e restaura o estado de auto-hide capturado. `--restore` recupera a barra sem iniciar a interface.

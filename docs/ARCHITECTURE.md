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
- `SystemStatusMonitor`: uma fonte compartilhada de rede, áudio, bateria e Bluetooth para todas as topbars.
- `QuickPanelWindow`: flyout transitório, criado somente quando aberto e totalmente descartado ao fechar.
- `Loc`: seleciona português, inglês ou espanhol pelo idioma de exibição do Windows, com fallback em inglês; data e hora usam independentemente o formato regional do usuário.

## Inicialização do usuário

O instalador registra `%LOCALAPPDATA%\Orla\Orla.exe --startup` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Assim que o Explorer inicia a sessão, a Orla lê as preferências persistidas, cria uma topbar e um dock para cada monitor, oculta as barras nativas e reinstala a integração da tecla Windows.

Antes de adquirir o mutex de instância única, uma cópia executada fora da pasta oficial transfere o início para o executável instalado. Isso cobre o recurso do Windows que reabre aplicativos da sessão anterior e evita que binários baixados ou temporários assumam o desktop. `--portable` desativa explicitamente esse redirecionamento para desenvolvimento e testes.

## Fluent Search multi-monitor

Cada botão informa o `Screen.DeviceName` de sua própria tela; a tecla Windows usa a tela do cursor. Antes de enviar `RequestOpen` pelo pipe local, Orla transfere o HWND ainda oculto para a área útil solicitada. Depois que o Fluent o torna visível, Orla não altera mais a posição: o próprio Fluent controla a transição entre a altura temporária e a barra compacta, sem um segundo salto vertical.

Os pedidos são serializados em vez de descartados. Se a busca está visível, o mesmo comando a oculta e a espera termina assim que esse estado é confirmado; um novo toque pode reabri-la imediatamente.

Essa ordem é importante: reexibir diretamente o HWND oculto produz uma janela visualmente aberta, mas sem o estado interno de pesquisa. Orla não simula o hotkey, não força uma janela oculta a aparecer e não reinicia à força o Fluent Search.

## Desempenho

Topbars opacas e docks transparentes usam renderização por software para manter previsível a memória em GPUs integradas. Foco, disponibilidade de rede e volume usam eventos, sem polling rápido. Bateria, Bluetooth e intensidade do Wi-Fi são consultados a cada 10 segundos por uma única instância compartilhada; o painel faz sua própria leitura lenta somente enquanto está aberto. O catálogo do dock roda a cada 2 segundos; borda e sobreposição usam cache.

O áudio usa `IAudioEndpointVolume` e seu callback nativo. A rede usa `NetworkChange`; quando a interface ativa é Wi-Fi, o RSSI vem de `WlanQueryInterface` e é convertido em uma qualidade de 0–100%. A consulta não solicita SSID, perfis salvos ou permissão de localização. Bluetooth é enumerado pelas APIs Win32 e o respectivo handle sempre é fechado na mesma leitura.

O painel mantém uma hierarquia visual simples: cartões de largura uniforme, título e estado em duas linhas, ícones dentro de caixas ópticas de 30×30, espaçamento constante, chevron apenas em ações navegáveis e controles WPF acessíveis por teclado/UI Automation. Rede, Bluetooth e energia encaminham para URIs `ms-settings:` específicas; volume e mute são ajustados diretamente. Topbar e painel compartilham os vetores oficiais Microsoft Fluent UI System Icons na grade nativa de 20×20; a bateria usa dez níveis e um estado próprio de carregamento. A topbar apresenta os estados em um único botão com `ControlFillColor` no hover/pressionamento; o conteúdo permanece imóvel no hover e usa escala somente durante o clique. O painel preserva a ação própria de cada cartão.

O dock mantém o histórico dos dois últimos aplicativos externos, ignorando suas próprias janelas e janelas auxiliares do mesmo processo. Ao minimizar o aplicativo ativo, Orla promove o aplicativo anterior e sincroniza imediatamente os indicadores dos dois monitores. Antes de ativar um app, resolve novamente seus HWNDs para não usar uma janela substituída desde o último refresh; uma recusa temporária de foco do Windows recebe tentativas curtas e limitadas.

## Multi-monitor

Cada `Screen.DeviceName` recebe uma topbar e um dock. A lista e a ordem são compartilhadas; posicionamento, área útil, auto-hide e destino do Fluent Search pertencem a cada tela.

## Reversibilidade

Appbars existem apenas durante o processo. Na saída, Orla remove os registros, mostra as taskbars nativas e restaura o estado de auto-hide capturado. `--restore` recupera a barra sem iniciar a interface.

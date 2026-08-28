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

## Inicialização do usuário

O instalador registra `%LOCALAPPDATA%\Orla\Orla.exe --startup` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Assim que o Explorer inicia a sessão, a Orla lê as preferências persistidas, cria uma topbar e um dock para cada monitor, oculta as barras nativas e reinstala a integração da tecla Windows.

Antes de adquirir o mutex de instância única, uma cópia executada fora da pasta oficial transfere o início para o executável instalado. Isso cobre o recurso do Windows que reabre aplicativos da sessão anterior e evita que binários baixados ou temporários assumam o desktop. `--portable` desativa explicitamente esse redirecionamento para desenvolvimento e testes.

## Fluent Search multi-monitor

Cada botão informa o `Screen.DeviceName` de sua própria tela; a tecla Windows usa a tela do cursor. Antes de enviar `RequestOpen` pelo pipe local, Orla transfere o HWND ainda oculto para a área útil solicitada. Depois que o Fluent o torna visível, Orla não altera mais a posição: o próprio Fluent controla a transição entre a altura temporária e a barra compacta, sem um segundo salto vertical.

Os pedidos são serializados em vez de descartados. Se a busca está visível, o mesmo comando a oculta e a espera termina assim que esse estado é confirmado; um novo toque pode reabri-la imediatamente.

Essa ordem é importante: reexibir diretamente o HWND oculto produz uma janela visualmente aberta, mas sem o estado interno de pesquisa. Orla não simula o hotkey, não força uma janela oculta a aparecer e não reinicia à força o Fluent Search.

## Desempenho

Topbars opacas e docks transparentes usam renderização por software para manter previsível a memória em GPUs integradas. Relógio/rede/bateria são atualizados a cada 15 segundos. Foco usa evento nativo; o catálogo do dock roda a cada 2 segundos; borda e sobreposição usam cache.

O dock mantém o histórico dos dois últimos aplicativos externos, ignorando suas próprias janelas e janelas auxiliares do mesmo processo. Ao minimizar o aplicativo ativo, Orla promove o aplicativo anterior e sincroniza imediatamente os indicadores dos dois monitores. Antes de ativar um app, resolve novamente seus HWNDs para não usar uma janela substituída desde o último refresh; uma recusa temporária de foco do Windows recebe tentativas curtas e limitadas.

## Multi-monitor

Cada `Screen.DeviceName` recebe uma topbar e um dock. A lista e a ordem são compartilhadas; posicionamento, área útil, auto-hide e destino do Fluent Search pertencem a cada tela.

## Reversibilidade

Appbars existem apenas durante o processo. Na saída, Orla remove os registros, mostra as taskbars nativas e restaura o estado de auto-hide capturado. `--restore` recupera a barra sem iniciar a interface.

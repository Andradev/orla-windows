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

## Fluent Search multi-monitor

Cada botão informa o `Screen.DeviceName` de sua própria tela; a tecla Windows usa a tela do cursor. Orla envia uma mensagem `RequestOpen` pelo pipe local da instância do Fluent Search. O próprio Fluent inicializa e focaliza o campo de busca; somente depois Orla centraliza o HWND já visível na área útil solicitada.

Essa ordem é importante: reexibir diretamente o HWND oculto produz uma janela visualmente aberta, mas sem o estado interno de pesquisa. Orla não simula o hotkey, não força uma janela oculta a aparecer e não reinicia à força o Fluent Search.

## Desempenho

Topbars opacas e docks transparentes usam renderização por software para manter previsível a memória em GPUs integradas. Relógio/rede/bateria são atualizados a cada 15 segundos. Foco usa evento nativo; o catálogo do dock roda a cada 2 segundos; borda e sobreposição usam cache.

O dock mantém o histórico dos dois últimos aplicativos externos, ignorando suas próprias janelas e janelas auxiliares do mesmo processo. Ao minimizar o aplicativo ativo, Orla promove o aplicativo anterior e sincroniza imediatamente os indicadores dos dois monitores.

## Multi-monitor

Cada `Screen.DeviceName` recebe uma topbar e um dock. A lista e a ordem são compartilhadas; posicionamento, área útil, auto-hide e destino do Fluent Search pertencem a cada tela.

## Reversibilidade

Appbars existem apenas durante o processo. Na saída, Orla remove os registros, mostra as taskbars nativas e restaura o estado de auto-hide capturado. `--restore` recupera a barra sem iniciar a interface.

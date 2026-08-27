# Arquitetura

VictorShell é um único executável WPF/.NET Framework, sem pacotes externos.

## Componentes

- `ShellController`: ciclo de vida, monitores, integração com Fluent Search e tecla Windows.
- `TopBarWindow`: appbar Win32 registrada em cada monitor para reservar a área superior.
- `DockWindow`: dock flutuante, auto-hide, agrupamento, clique, arrasto e indicadores.
- `WindowCatalog`: enumeração de janelas visíveis e metadados de processo.
- `ForegroundWindowTracker`: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` para atualizar foco sem polling rápido.
- `BareWindowsKeyHook`: hook de teclado de baixo nível. Suprime apenas `Win` isolado e reinsere `Win` quando outra tecla forma uma combinação.
- `TaskbarController`: oculta e restaura taskbars nativas de forma reversível.
- `ShellSettings`: arquivo INI simples no perfil do usuário.

## Desempenho

Topbars opacas e docks transparentes usam renderização de software porque certas GPUs integradas reservam centenas de MB em composição WPF acelerada. Status de relógio/rede/bateria é atualizado a cada 15 segundos e foco usa evento nativo. A varredura de janelas do dock ocorre a cada 2 segundos; a detecção de borda/overlap usa cache.

## Multi-monitor

Cada `Screen.DeviceName` recebe uma topbar e um dock. A lista de aplicativos e a ordem persistida são compartilhadas, enquanto posicionamento e auto-hide usam os limites físicos de cada tela.

## Reversibilidade

O controlador registra appbars somente enquanto está vivo. Na saída, remove registros, mostra taskbars nativas e restaura o estado de auto-hide capturado. `--restore` permite recuperar a barra sem iniciar a interface.

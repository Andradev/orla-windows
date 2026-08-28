# Orla 1.2.6

Controles rápidos menores, informativos e sem ações visuais duplicadas.

- reorganiza Wi-Fi, Bluetooth, Economia de energia e Luz noturna em uma grade 2×2 com hover dividido uniforme;
- sinaliza ligado/desligado em Economia de energia e Luz noturna, sem escrever em `CloudStore` ou políticas do Windows;
- substitui os cartões grandes por duas linhas independentes de ícone + slider para volume e brilho;
- mostra a porcentagem atual ao manter o mouse sobre o ícone ou o slider de volume;
- remove o botão textual de mute/unmute e o cartão redundante de bateria/“Charging”;
- corrige o recorte de 2 px na borda direita do painel;
- mostra, com reticências quando necessário, o nome da rede Wi-Fi e do dispositivo Bluetooth conectado;
- mantém Bluetooth exclusivamente no painel e atualiza o cartão logo após ativar ou desativar;
- faz o segundo clique no chevron fechar a bandeja oculta e reduz o aparecimento transitório na posição da taskbar;
- inicia o dock sem aplicativos fixados por padrão;
- mantém Wi-Fi e Bluetooth como toggles reais pela API oficial e preserva dock, topbars e comportamento multi-monitor.

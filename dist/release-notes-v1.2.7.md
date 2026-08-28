# Orla 1.2.7

Correção visual focada na abertura dos ícones ocultos.

- impede que o flyout nativo apareça por um instante na posição inferior da taskbar;
- intercepta somente durante a abertura os eventos de criação, exibição e posição da janela nativa;
- mantém uma região de desenho vazia enquanto o Windows executa o posicionamento original;
- restaura o conteúdo somente depois que o flyout já está alinhado à topbar;
- preserva o segundo clique para fechar, a animação do chevron e o comportamento nas duas telas.

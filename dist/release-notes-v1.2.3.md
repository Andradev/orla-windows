# Orla 1.2.3

Refinamento nativo dos indicadores e da localização do Windows.

- substituição dos ícones Lucide pelos Microsoft Fluent UI System Icons oficiais de 20 px;
- bateria reconstruída com contorno integrado, dez níveis discretos e símbolo próprio durante o carregamento;
- Wi-Fi, volume, Bluetooth, busca, configurações, fechar e chevrons alinhados na mesma família visual;
- hover e pressionamento dos botões usam os tokens escuros `SubtleFillColorSecondary` e `SubtleFillColorTertiary` do WinUI;
- o conjunto de indicadores continua sendo um único botão, sem cápsula permanente;
- células ópticas mais largas dão respiro uniforme entre Wi-Fi, som, bateria, percentual e Bluetooth;
- idioma da interface passa a seguir o idioma de exibição do Windows, sem confundir layouts de teclado com idioma;
- o relógio preserva o formato textual compacto com mês por nome, usando a ordem e o padrão de 12/24 horas da região do Windows.

Validado em dois monitores, com estados reais de rede, bateria e volume, alternância de mute, quatro faixas de volume e doze ciclos de abertura/fechamento do painel após o aquecimento, sem crescimento de handles.

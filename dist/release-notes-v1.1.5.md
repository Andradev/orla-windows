Inicialização integrada e protegida no login do Windows:

- registra a instalação oficial com o modo explícito `--startup`;
- restaura automaticamente preferências, topbars, docks e integração da tecla Windows;
- redireciona cópias baixadas ou temporárias para `%LOCALAPPDATA%\Orla\Orla.exe` antes da instância única;
- impede que o recurso de reabrir aplicativos do Windows faça uma cópia de teste assumir o desktop;
- oferece `--portable` como substituição explícita para desenvolvimento e testes.

Validado no Windows 11 com dois monitores. O teste confirmou o redirecionamento da cópia externa, o modo portátil, a preservação do `settings.ini` e a criação automática das duas topbars e dos dois docks.

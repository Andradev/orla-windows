# Segurança

## Relatar uma vulnerabilidade

Abra um Security Advisory privado no GitHub do projeto. Evite publicar detalhes exploráveis em uma issue antes da correção.

## Modelo de segurança

- execução no contexto do usuário atual;
- nenhuma elevação solicitada pelo instalador;
- nenhuma modificação de política, serviço, driver ou arquivo do sistema;
- hook global apenas para distinguir `Win` sozinho de combinações, ativado somente quando Fluent Search existe;
- restauração explícita da barra nativa ao sair/desinstalar;
- configurações locais em `%LOCALAPPDATA%\VictorShell`.

O executável gerado por `Build.ps1` não é assinado. Verifique o código-fonte e compile localmente para maior confiança.

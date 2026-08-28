# Segurança

## Versões com suporte

Somente a [release estável mais recente](https://github.com/Andradev/orla-windows/releases/latest) recebe correções de segurança. Versões anteriores devem ser atualizadas antes da investigação.

## Relatar uma vulnerabilidade

Use o botão **Report a vulnerability** na [página de segurança do repositório](https://github.com/Andradev/orla-windows/security/advisories/new). Inclua versão, impacto, passos mínimos de reprodução e uma sugestão de mitigação, quando houver.

Não publique detalhes exploráveis em issues ou discussões antes de uma correção. O mantenedor confirmará o recebimento e coordenará a divulgação pelo advisory privado; prazos dependem da gravidade e da reprodutibilidade.

## Modelo de segurança

- execução no contexto do usuário atual;
- nenhuma elevação solicitada pelo instalador;
- nenhuma modificação de política, serviço, driver ou arquivo do sistema;
- hook global apenas para distinguir `Win` sozinho de combinações, ativado somente quando Fluent Search existe;
- restauração explícita da barra nativa ao sair/desinstalar;
- configurações locais em `%LOCALAPPDATA%\Orla`.

O executável gerado por `Build.ps1` não é assinado. Verifique o código-fonte e compile localmente para maior confiança.

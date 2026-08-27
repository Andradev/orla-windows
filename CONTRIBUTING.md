# Contribuindo

Obrigado pelo interesse no VictorShell.

1. Crie um fork e uma branch curta para a mudança.
2. Execute `Build.ps1` em Windows x64.
3. Teste restauração da barra com `dist\VictorShell.exe --restore`.
4. Em mudanças de dock, valide clique, minimizar/restaurar, arrasto e dois monitores.
5. Não inclua caminhos de usuário, tokens, logs, `settings.ini` ou binários não reproduzíveis.
6. Abra um pull request explicando comportamento anterior, novo comportamento e teste executado.

Mantenha a proposta do projeto: pequeno, reversível, sem serviços, injeção em outros processos ou alteração de arquivos do Windows.

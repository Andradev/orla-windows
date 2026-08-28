# Testes

## Verificação rápida

Em Windows 10/11 x64 com .NET Framework 4.8:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Verify-Repository.ps1
```

O verificador compila o executável em uma pasta temporária, confirma a versão,
valida todos os SVGs e detecta links locais quebrados nos arquivos Markdown.

## Matriz funcional mínima

### Dock

- revelar por qualquer ponto da borda inferior de cada monitor;
- ativar, minimizar e restaurar o mesmo app repetidamente;
- alternar entre apps Win32, Teams/WebView e aplicativos com mais de uma janela;
- clicar próximo às quatro bordas do botão;
- reorganizar por arrastar sem disparar um clique;
- validar indicador azul, menu de contexto e Lixeira.

### Topbar e painel

- conferir idioma, relógio 12/24 horas e data regional;
- alternar Wi-Fi, Bluetooth e mute e reler o estado real;
- verificar bateria, brilho integrado e DDC/CI quando disponível;
- abrir e fechar ícones ocultos duas vezes seguidas;
- confirmar que `Win+Shift+S`, `Win+E` e outros atalhos permanecem nativos.

### Multi-monitor

- testar escalas de DPI iguais e diferentes;
- maximizar janelas em cada tela e conferir a área reservada da AppBar;
- remover/reconectar uma tela e reiniciar a sessão;
- confirmar listas e ordem iguais, com posicionamento independente.

## Desempenho

```powershell
.\tools\Measure-Performance.ps1 -ProcessName Orla -DurationSeconds 600
```

Registre hardware, versão do Windows, número de monitores, DPI, duração e carga
de trabalho. Resultados isolados não devem ser apresentados como garantia universal.

## Release

1. Atualize versão, `CHANGELOG.md` e notas em `dist\release-notes-vX.Y.Z.md`.
2. Execute o verificador e os testes funcionais.
3. Crie uma tag anotada `vX.Y.Z`.
4. O workflow de release recompila, gera SHA-256 e publica os artefatos.
5. Baixe a release e valide `Orla.exe.sha256` antes de marcá-la como estável.

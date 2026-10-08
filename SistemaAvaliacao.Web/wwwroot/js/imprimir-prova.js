// Impressão da prova física (Admin/ImprimirProva). Ver CONVENTIONS.md §7.
// Aciona o diálogo de impressão automaticamente ao abrir a página.
window.addEventListener('load', function () {
    // Pequeno delay para garantir que as imagens já carregaram.
    setTimeout(function () { window.print(); }, 600);
});

// Detalhes da prova (Admin/DetalhesProva). Ver CONVENTIONS.md §7.
// Dados do servidor chegam via data-* em #detalhesProvaConfig e pelo
// campo antiforgery renderizado na página.

(function () {
    var cfg = document.getElementById('detalhesProvaConfig');
    var provaId = cfg ? cfg.dataset.provaId : '';
    var urlImportarAlunos = cfg ? cfg.dataset.urlImportarAlunos : '';
    var urlEditar = cfg ? cfg.dataset.urlEditar : '';
    var msgSucesso = cfg ? cfg.dataset.sucesso : '';
    var msgErro = cfg ? cfg.dataset.erro : '';

    function antiForgeryToken() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    // Chamada via onclick no markup — precisa ser global.
    window.copiarLink = function () {
        navigator.clipboard.writeText(document.getElementById('linkPublico').value)
            .then(function () { toastr.success('Link copiado!'); });
    };

    $(function () {
        // Upload automático ao selecionar CSV
        document.getElementById('inputCsvAlunos')?.addEventListener('change', function () {
            const file = this.files[0];
            if (!file) return;

            const formData = new FormData();
            formData.append('provaId', provaId);
            formData.append('arquivoCsv', file);
            formData.append('__RequestVerificationToken', antiForgeryToken());

            // Fecha o modal de alunos
            bootstrap.Modal.getInstance('#modalAlunos')?.hide();

            fetch(urlImportarAlunos, {
                method: 'POST',
                body: formData
            })
            .then(r => {
                if (r.redirected || r.ok) {
                    // Sucesso — recarrega para refletir os novos alunos
                    window.location.reload();
                } else {
                    return r.text().then(msg => { throw new Error(msg || 'Erro desconhecido.'); });
                }
            })
            .catch(err => {
                document.getElementById('erroImportacaoMsg').textContent =
                    'Confira o arquivo CSV e tente novamente.';
                new bootstrap.Modal('#modalErroImportacao').show();
            });

            // Limpa o input para permitir re-seleção do mesmo arquivo
            this.value = '';
        });

        $(document).on('click', '.btn-remover-aluno', function () {
            $('#removerAlunoId').val($(this).data('id'));
            $('#modalNomeAluno').text($(this).data('nome'));
            new bootstrap.Modal('#modalRemoverAluno').show();
        });

        // Abre o modal de edição (campos já preenchidos pelo servidor).
        $('#btnAbrirEdicao').on('click', function () {
            $('#editarErro').addClass('d-none').text('');
            new bootstrap.Modal('#modalEditarProva').show();
        });

        $('#btnSalvarEdicao').on('click', function () {
            const nome = $('#editNome').val().trim();
            if (!nome) { $('#editarErro').removeClass('d-none').text('Nome é obrigatório.'); return; }

            const body = new URLSearchParams({
                Id: provaId,
                Nome: nome,
                Descricao: $('#editDescricao').val(),
                Instituicao: $('#editInstituicao').val(),
                NumeroAlternativas: $('#editAlternativas').val(),
                DataInicio: $('#editDataInicio').val(),
                DataFim: $('#editDataFim').val(),
                __RequestVerificationToken: antiForgeryToken()
            });

            $('#btnSalvarEdicao').prop('disabled', true);
            fetch(urlEditar, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: body.toString()
            })
            .then(async r => {
                if (!r.ok) {
                    const erros = await r.json();
                    $('#editarErro').removeClass('d-none').text(Array.isArray(erros) ? erros.join(' ') : 'Erro ao salvar.');
                    return;
                }
                const data = await r.json();
                $('#tituloProva').text(data.nome);
                $('#descricaoProva').text(data.descricao ?? '');
                // Atualiza a instituição exibida na linha de metadados (reconstrói o trecho).
                var inst = (data.instituicao ?? '').trim();
                if (inst) {
                    $('#metaInstituicao').html('<span class="mx-2">·</span><i class="fas fa-building me-1"></i><span id="textoInstituicao"></span>');
                    $('#textoInstituicao').text(inst);
                } else {
                    $('#metaInstituicao').empty();
                }
                document.title = data.nome;
                bootstrap.Modal.getInstance('#modalEditarProva').hide();
                toastr.success('Prova atualizada!');
            })
            .finally(() => $('#btnSalvarEdicao').prop('disabled', false));
        });

        // ── Mensagens de feedback (TempData vindos do servidor) ──
        if (msgSucesso) toastr.success(msgSucesso);
        if (msgErro) toastr.error(msgErro, '', { timeOut: 10000 });
    });
})();

// Gestão de questões da prova (Admin/QuestoesProva). Ver CONVENTIONS.md §7.
// Dados do servidor chegam via data-* no elemento #questoesProvaConfig.
$(function () {
    var cfg = document.getElementById('questoesProvaConfig');
    var obterQuestaoUrl = cfg ? cfg.dataset.obterQuestaoUrl : '';
    var msgSucesso = cfg ? cfg.dataset.sucesso : '';
    var msgErro = cfg ? cfg.dataset.erro : '';

    var $form = $('#formQuestao');

    function toggleAlternativas() {
        var n = parseInt($('#fieldNumAlt').val());
        $('.alt-row').each(function () {
            var idx = parseInt($(this).data('index'));
            $(this).toggle(idx < n);
            if (idx >= n) $(this).find('input[type=text]').val('');
        });
    }
    $('#fieldNumAlt').on('change', toggleAlternativas);
    toggleAlternativas();

    // ── Preview de imagem ao selecionar arquivo ──
    $('#fieldImagem').on('change', function () {
        var file = this.files[0];
        if (file) {
            var reader = new FileReader();
            reader.onload = function (e) {
                $('#imagemPreviewImg').attr('src', e.target.result);
                $('#imagemPreview').show();
                $('#imagemExistente').hide();
            };
            reader.readAsDataURL(file);
        } else {
            $('#imagemPreview').hide();
        }
    });

    $('#btnRemoverImagem').on('click', function () {
        $('#fieldImagem').val('');
        $('#imagemPreview').hide();
    });

    // ── Importar da planilha (.xlsx) ──
    $('#formImportarXlsx').on('submit', function () {
        $('#btnEnviarImportarXlsx').prop('disabled', true)
            .html('<i class="fas fa-spinner fa-spin me-1"></i>Lendo planilha...');
    });

    // ── Nova questão ──
    $('#btnNovaQuestao').on('click', function () {
        resetForm();
        $('#formTitulo').html('<i class="fas fa-plus-circle me-1"></i>Nova Questão');
        $('#btnSalvarTexto').text('Adicionar Questão');
        $form.slideDown(200);
        $('#fieldComando').focus();
        $('html,body').animate({ scrollTop: $form.offset().top - 80 }, 300);
    });

    $('#btnCancelarForm, #btnCancelarForm2').on('click', function () {
        $form.slideUp(200);
    });

    // ── Editar questão ──
    $(document).on('click', '.btn-editar', function () {
        var id = $(this).data('id');
        $.getJSON(obterQuestaoUrl, { id: id }, function (q) {
            resetForm();
            $('#fieldQuestaoId').val(q.id);
            $('#fieldEnunciado').val(q.enunciado || '');
            $('#fieldComando').val(q.comando);
            $('#fieldAltA').val(q.alternativaA);
            $('#fieldAltB').val(q.alternativaB);
            $('#fieldAltC').val(q.alternativaC || '');
            $('#fieldAltD').val(q.alternativaD || '');
            $('#fieldAltE').val(q.alternativaE || '');
            $('#fieldNumAlt').val(q.numeroAlternativas);
            $('input[name=AlternativaCorreta][value=' + q.alternativaCorreta + ']').prop('checked', true);
            toggleAlternativas();

            // Mostrar imagem existente se houver
            if (q.imagemUrl) {
                $('#imagemExistenteImg').attr('src', q.imagemUrl);
                $('#imagemExistente').show();
            }

            $('#formTitulo').html('<i class="fas fa-pen me-1"></i>Editar Questão');
            $('#btnSalvarTexto').text('Salvar Alterações');
            $form.slideDown(200);
            $('html,body').animate({ scrollTop: $form.offset().top - 80 }, 300);
            $('#fieldComando').focus();
        });
    });

    // ── Excluir ──
    $(document).on('click', '.btn-excluir', function () {
        $('#excluirQuestaoId').val($(this).data('id'));
        $('#modalOrdem').text($(this).data('ordem'));
        var cmd = $(this).data('comando');
        $('#modalComando').text(cmd.length > 120 ? cmd.substring(0, 120) + '...' : cmd);
        new bootstrap.Modal('#modalExcluir').show();
    });

    function resetForm() {
        $('#formSalvar')[0].reset();
        $('#fieldQuestaoId').val('');
        $('#fieldNumAlt').val(5);
        $('input[name=AlternativaCorreta][value=A]').prop('checked', true);
        $('#imagemPreview').hide();
        $('#imagemExistente').hide();
        toggleAlternativas();
    }

    // ── Mensagens de feedback (TempData vindos do servidor) ──
    if (msgSucesso) toastr.success(msgSucesso);
    if (msgErro) toastr.error(msgErro);
});

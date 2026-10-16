// Fluxo de responder prova — tela do aluno (Exame/Responder). Ver CONVENTIONS.md §7.
// FLUXO CRÍTICO: lógica preservada verbatim da view original.
// Dados do servidor chegam via data-* em #exameResponderConfig; o token
// antiforgery é lido do campo @Html.AntiForgeryToken() presente na página.
(function () {
    const cfg = document.getElementById('exameResponderConfig');
    const token = cfg.dataset.token;
    const tentativa = cfg.dataset.tentativa;
    const dataFim = new Date(cfg.dataset.dataFim);
    const totalQuestoes = parseInt(cfg.dataset.totalQuestoes, 10);
    let ordemAtual = 1;
    let finalizando = false;
    let salvamentosPendentes = 0;

    $(function () {
        function mostrarStatus(texto, classe) {
            $('#statusSalvamento').text(texto).removeClass('salvo erro').addClass(classe || '');
        }

        function mostrarMensagem(texto, tipo) {
            if (typeof toastr !== 'undefined' && typeof toastr[tipo] === 'function') {
                toastr[tipo](texto);
            } else {
                mostrarStatus(texto, tipo === 'error' ? 'erro' : '');
            }
        }

        function atualizarProgresso(totalRespondidas) {
            $('#textoProgresso').text(totalRespondidas + ' de ' + totalQuestoes + ' respondidas');
            const percentual = totalQuestoes === 0 ? 0 : Math.round(totalRespondidas * 100 / totalQuestoes);
            $('#barraProgresso').css('width', percentual + '%');
        }

        const lightbox = document.getElementById('imagemLightbox');
        const lightboxImg = document.getElementById('imagemLightboxImg');
        let imagemZoom = 1;
        let imagemX = 0;
        let imagemY = 0;
        let distanciaInicial = 0;
        let zoomInicial = 1;
        let arrastandoImagem = false;
        let inicioArrastoX = 0;
        let inicioArrastoY = 0;
        let imagemInicioX = 0;
        let imagemInicioY = 0;

        function atualizarImagemLightbox() {
            lightboxImg.style.transform = `translate3d(${imagemX}px, ${imagemY}px, 0) scale(${imagemZoom})`;
        }

        function limitarZoom(valor) {
            return Math.min(4, Math.max(1, valor));
        }

        function abrirImagem(src, alt) {
            lightboxImg.src = src;
            lightboxImg.alt = alt || 'Imagem ampliada';
            imagemZoom = 1;
            imagemX = 0;
            imagemY = 0;
            atualizarImagemLightbox();
            lightbox.classList.add('aberta');
            document.body.style.overflow = 'hidden';
        }

        function fecharImagem() {
            lightbox.classList.remove('aberta');
            lightboxImg.src = '';
            document.body.style.overflow = '';
        }

        // Delegado: a imagem da questão é recriada a cada navegação (lazy loading).
        $(document).on('click', '#questao-atual img', function () {
            abrirImagem(this.src, this.alt);
        });
        $('#fecharImagemLightbox').on('click', fecharImagem);
        $(lightbox).on('click', function (event) {
            if (event.target === lightbox) fecharImagem();
        });
        $(document).on('keydown', function (event) {
            if (event.key === 'Escape' && lightbox.classList.contains('aberta')) fecharImagem();
        });

        lightboxImg.addEventListener('wheel', function (event) {
            event.preventDefault();
            imagemZoom = limitarZoom(imagemZoom + (event.deltaY < 0 ? .25 : -.25));
            if (imagemZoom === 1) { imagemX = 0; imagemY = 0; }
            atualizarImagemLightbox();
        }, { passive: false });

        lightboxImg.addEventListener('touchstart', function (event) {
            if (event.touches.length === 2) {
                distanciaInicial = Math.hypot(
                    event.touches[0].clientX - event.touches[1].clientX,
                    event.touches[0].clientY - event.touches[1].clientY);
                zoomInicial = imagemZoom;
                arrastandoImagem = false;
            } else if (event.touches.length === 1 && imagemZoom > 1) {
                arrastandoImagem = true;
                inicioArrastoX = event.touches[0].clientX;
                inicioArrastoY = event.touches[0].clientY;
                imagemInicioX = imagemX;
                imagemInicioY = imagemY;
            }
        }, { passive: false });

        lightboxImg.addEventListener('touchmove', function (event) {
            event.preventDefault();
            if (event.touches.length === 2 && distanciaInicial > 0) {
                const distanciaAtual = Math.hypot(
                    event.touches[0].clientX - event.touches[1].clientX,
                    event.touches[0].clientY - event.touches[1].clientY);
                imagemZoom = limitarZoom(zoomInicial * distanciaAtual / distanciaInicial);
                if (imagemZoom === 1) { imagemX = 0; imagemY = 0; }
            } else if (arrastandoImagem && event.touches.length === 1) {
                imagemX = imagemInicioX + event.touches[0].clientX - inicioArrastoX;
                imagemY = imagemInicioY + event.touches[0].clientY - inicioArrastoY;
            }
            atualizarImagemLightbox();
        }, { passive: false });

        lightboxImg.addEventListener('touchend', function () {
            distanciaInicial = 0;
            arrastandoImagem = false;
        });

        // Cronômetro
        //
        // A VERDADE do tempo é o servidor (DataFim - UtcNow), nunca o relógio da máquina
        // do aluno — que pode estar adiantado ou atrasado. Por isso sincronizamos com o
        // endpoint /tempo-restante periodicamente e, entre as sincronizações, apenas
        // decrementamos localmente para a contagem ficar suave (sem pular de segundo a
        // segundo conforme a rede). dataFim é usado só como estimativa inicial até a
        // primeira sincronização chegar.
        const INTERVALO_SYNC_MS = 20000;   // re-sincroniza com o servidor a cada 20s
        const SEGUNDOS_AVISO = 60;         // avisa quando faltar 1 minuto
        let segundosRestantes = Math.max(0, Math.floor((dataFim - new Date()) / 1000));
        let avisoUmMinutoExibido = false;
        let tempoEncerrado = false;

        function formatarTempo(totalSegundos) {
            const horas = Math.floor(totalSegundos / 3600);
            const min = Math.floor((totalSegundos % 3600) / 60);
            const seg = totalSegundos % 60;
            if (horas > 0) {
                return horas + ':' + String(min).padStart(2, '0') + ':' + String(seg).padStart(2, '0');
            }
            return String(min).padStart(2, '0') + ':' + String(seg).padStart(2, '0');
        }

        function renderizarCronometro() {
            $('#cronometro').text(formatarTempo(Math.max(0, segundosRestantes)));
        }

        function aoEncerrarTempo() {
            if (tempoEncerrado) return;
            tempoEncerrado = true;
            segundosRestantes = 0;
            renderizarCronometro();
            // Corte limpo: não aceita mais marcações. O servidor também recusa qualquer
            // resposta que chegue após o DataFim, então nada enviado tarde é aceito.
            $('.alt-radio').prop('disabled', true);
            if (!finalizando) {
                mostrarMensagem('O tempo da prova terminou. Suas respostas salvas foram registradas.', 'error');
                finalizarProva();
            }
        }

        // Tique local a cada segundo: contagem suave entre as sincronizações.
        function tiqueLocal() {
            if (tempoEncerrado) return;
            segundosRestantes = Math.max(0, segundosRestantes - 1);
            renderizarCronometro();

            if (!avisoUmMinutoExibido && segundosRestantes > 0 && segundosRestantes <= SEGUNDOS_AVISO) {
                avisoUmMinutoExibido = true;
                mostrarMensagem('Falta 1 minuto! Revise suas respostas e finalize a prova.', 'warning');
            }

            if (segundosRestantes <= 0) {
                aoEncerrarTempo();
            }
        }

        // Sincronização com o servidor: ajusta a base de tempo pela verdade autoritativa.
        function sincronizarTempo() {
            if (tempoEncerrado) return;
            $.getJSON(`/prova/${token}/tempo-restante?tentativa=${encodeURIComponent(tentativa)}`)
                .done(function (resp) {
                    if (!resp) return;
                    if (resp.encerrada || resp.segundosRestantes <= 0) {
                        aoEncerrarTempo();
                        return;
                    }
                    // Confia no servidor: corrige qualquer deriva do relógio local.
                    segundosRestantes = resp.segundosRestantes;
                    renderizarCronometro();
                    if (!avisoUmMinutoExibido && segundosRestantes <= SEGUNDOS_AVISO) {
                        avisoUmMinutoExibido = true;
                        mostrarMensagem('Falta 1 minuto! Revise suas respostas e finalize a prova.', 'warning');
                    }
                });
            // Falha de rede aqui é ignorada de propósito: a contagem local segue e a
            // próxima tentativa de sincronização corrige. O corte final é garantido pelo
            // servidor, que recusa respostas após o DataFim.
        }

        renderizarCronometro();
        sincronizarTempo();                 // sincroniza já na carga
        setInterval(tiqueLocal, 1000);
        setInterval(sincronizarTempo, INTERVALO_SYNC_MS);

        // Navegação
        function atualizarPontoAtual(ordem) {
            $('.status-dot').each(function () {
                const respondida = $(this).data('respondida') === true;
                $(this).removeClass('status-dot-current status-dot-pending status-dot-responded')
                    .addClass(respondida ? 'status-dot-responded' : 'status-dot-pending');
            });

            const pontoAtual = $(`.status-dot[data-ordem="${ordem}"]`);
            if (pontoAtual.data('respondida') !== true) {
                pontoAtual.removeClass('status-dot-pending').addClass('status-dot-current');
            }
        }

        function atualizarFaixaSuperior(ordem) {
            const janela = 5;
            const inicio = Math.max(1, Math.min(ordem - 2, totalQuestoes - janela + 1));
            const fim = Math.min(totalQuestoes, inicio + janela - 1);

            $('.mobile-question-dots .status-dot').each(function () {
                const numero = Number($(this).data('ordem'));
                $(this).toggle(numero >= inicio && numero <= fim);
            });
        }

        // Cache no cliente dos fragmentos de questão já baixados (ordem -> html),
        // para não refazer a requisição ao voltar numa questão já vista.
        const cacheQuestoes = {};
        let carregandoQuestao = false;

        function atualizarDestaqueNavegacao(ordem) {
            $('.questao-nav-btn').removeClass('atual').attr('aria-current', 'false');
            $(`.questao-nav-btn[data-ordem="${ordem}"]`).addClass('atual').attr('aria-current', 'step');
            atualizarPontoAtual(ordem);
            atualizarFaixaSuperior(ordem);
        }

        // Lazy loading: busca o fragmento da questão no servidor (ou no cache local)
        // e o injeta no container. A primeira questão já veio renderizada no HTML.
        async function irParaQuestao(ordem) {
            if (carregandoQuestao || ordem === ordemAtual) return;
            if (ordem < 1 || ordem > totalQuestoes) return;

            const container = document.getElementById('questao-atual');

            if (cacheQuestoes[ordem]) {
                container.innerHTML = cacheQuestoes[ordem];
            } else {
                carregandoQuestao = true;
                $('#questao-carregando').prop('hidden', false);
                container.setAttribute('hidden', '');
                try {
                    const resp = await fetch(
                        `/prova/${token}/questao/${ordem}?tentativa=${encodeURIComponent(tentativa)}`,
                        { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                    if (!resp.ok) throw new Error('status ' + resp.status);
                    const html = await resp.text();
                    cacheQuestoes[ordem] = html;
                    container.innerHTML = html;
                } catch (e) {
                    mostrarMensagem('Não foi possível carregar a questão. Tente novamente.', 'error');
                    return;
                } finally {
                    carregandoQuestao = false;
                    $('#questao-carregando').prop('hidden', true);
                    container.removeAttribute('hidden');
                }
            }

            ordemAtual = ordem;
            atualizarDestaqueNavegacao(ordem);
            $('html, body').animate({ scrollTop: 0 }, 300);
        }

        // Handlers delegados: o conteúdo da questão é recriado a cada navegação,
        // então os botões internos precisam de delegação a partir do container.
        $(document).on('click', '.btn-proxima', function () {
            irParaQuestao(ordemAtual + 1);
        });
        $(document).on('click', '.btn-anterior', function () {
            irParaQuestao(ordemAtual - 1);
        });
        $(document).on('click', '.questao-nav-btn, .btn-ir-questao', function () {
            irParaQuestao(Number($(this).data('ordem')));
        });

        atualizarFaixaSuperior(ordemAtual);

        $('#btnVerTodasQuestoes').on('click', function () {
            atualizarModalProgresso();
            new bootstrap.Modal('#modalVisaoGeral').show();
        });

        // Limpar resposta (delegado — o botão é recriado a cada navegação).
        $(document).on('click', '.btn-limpar', function () {
            const questaoId = $(this).data('questao-id');
            const ordem = Number($(this).data('ordem')) || ordemAtual;
            const botao = this;
            if (salvamentosPendentes > 0 || finalizando) return;
            $(botao).prop('disabled', true);
            mostrarStatus('Removendo resposta...', '');

            $.post(`/prova/${token}/limpar-resposta`, {
                questaoId: questaoId,
                tentativa: tentativa,
                __RequestVerificationToken: $('[name=__RequestVerificationToken]').val()
            })
            .done(function (resp) {
                if (!resp.sucesso) {
                    mostrarMensagem(resp.erro || 'Não foi possível limpar a resposta.', 'error');
                    $(botao).prop('disabled', false);
                    return;
                }

                $(`input[name="questao_${questaoId}"]:checked`).prop('checked', false);
                // O fragmento em cache ficou desatualizado: invalida para rebuscar ao voltar.
                delete cacheQuestoes[ordem];
                $(`.questao-nav-btn[data-ordem="${ordem}"]`).removeClass('respondida');
                $(`.status-dot[data-ordem="${ordem}"]`).data('respondida', false);
                atualizarPontoAtual(ordemAtual);
                atualizarProgresso(resp.totalRespondidas);
                mostrarStatus('Resposta removida.', 'salvo');
            })
            .fail(function () {
                mostrarMensagem('Falha de conexão ao remover a resposta.', 'error');
                $(botao).prop('disabled', false);
            });
        });

        // Salvar resposta (delegado — o rádio é recriado a cada navegação).
        $(document).on('change', '.alt-radio', function () {
            const questaoId = $(this).data('questao-id');
            const alternativa = $(this).val();
            const ordem = Number($(this).data('ordem'));

            salvamentosPendentes++;
            mostrarStatus('Salvando resposta...', '');
            $.post(`/prova/${token}/salvar-resposta`, {
                questaoId: questaoId,
                alternativa: alternativa,
                tentativa: tentativa,
                __RequestVerificationToken: $('[name=__RequestVerificationToken]').val()
            })
            .done(function (resp) {
                salvamentosPendentes = Math.max(0, salvamentosPendentes - 1);
                if (resp.sucesso) {
                    // Atualiza o fragmento em cache para refletir a marcação ao voltar.
                    delete cacheQuestoes[ordem];
                    $(`.questao-nav-btn[data-ordem="${ordem}"]`).addClass('respondida');
                    $(`.status-dot[data-ordem="${ordem}"]`)
                        .data('respondida', true)
                        .removeClass('status-dot-current status-dot-pending')
                        .addClass('status-dot-responded');
                    atualizarProgresso(resp.totalRespondidas);
                    $(`.btn-limpar[data-questao-id="${questaoId}"]`).prop('disabled', false);
                    mostrarStatus('Resposta salva.', 'salvo');
                } else {
                    mostrarMensagem(resp.erro || 'Erro ao salvar resposta.', 'error');
                }
            })
            .fail(function (xhr) {
                salvamentosPendentes = Math.max(0, salvamentosPendentes - 1);
                mostrarMensagem('Erro ao salvar resposta. Verifique sua conexão.', 'error');
            });
        });

        // Finalizar
        $('.btn-finalizar').on('click', function () {
            if (salvamentosPendentes > 0) {
                mostrarMensagem('Aguarde o salvamento das respostas terminar.', 'warning');
                return;
            }
            const respondidas = $('.questao-nav-btn.respondida').length;
            if (respondidas < totalQuestoes) {
                if (!confirm(`Você respondeu ${respondidas} de ${totalQuestoes} questões. Deseja finalizar mesmo assim?`)) {
                    return;
                }
            }
            finalizarProva();
        });

        function finalizarProva() {
            if (finalizando) return;

            // Se há respostas ainda em trânsito, espera um instante por elas antes de
            // fechar. São envios que podem ter saído ANTES do DataFim (portanto legítimos);
            // dar essa breve janela evita descartar a última marcação do aluno. Mesmo assim,
            // o servidor é quem decide: só grava o que chegou dentro do prazo.
            if (salvamentosPendentes > 0) {
                $('.alt-radio').prop('disabled', true);
                setTimeout(finalizarProva, 400);
                return;
            }

            finalizando = true;
            $('.btn-finalizar, .btn-proxima, .btn-anterior, .btn-limpar, .alt-radio').prop('disabled', true);
            $('#btnFinalizar').prop('disabled', true).html('Finalizando...');

            $.post(`/prova/${token}/finalizar`, {
                tentativa: tentativa,
                __RequestVerificationToken: $('[name=__RequestVerificationToken]').val()
            })
            .done(function (resp) {
                if (resp.sucesso) {
                    // Redirecionar para tela de confirmação
                    window.location.href = '/prova/obrigado';
                } else {
                    finalizando = false;
                    mostrarMensagem(resp.erro || 'Erro ao finalizar.', 'error');
                    $('#btnFinalizar').prop('disabled', false).html('✓ Finalizar Prova');
                }
            })
            .fail(function (xhr) {
                finalizando = false;
                mostrarMensagem('Erro ao finalizar a prova. Tente novamente.', 'error');
                $('#btnFinalizar').prop('disabled', false).html('✓ Finalizar Prova');
            });
        }

        function atualizarModalProgresso() {
            const respondidas = $('.alt-radio:checked').length;
            $('#modalTotalRespondidas').text(respondidas);
        }
    });
})();
